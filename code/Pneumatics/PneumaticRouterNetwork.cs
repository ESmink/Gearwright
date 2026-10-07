using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

public sealed partial class PneumaticNetworkSystem
{
    private int searchBudget;
    internal bool ConfigureRouter(BlockEntityPneumaticTransport host, PneumaticRouterConfiguration configuration)
    {
        if (!configuration.Valid || configuration.Revision == int.MaxValue || configuration.Revision != host.Router.Configuration.Revision)
        { SetStatus(host, "config-stale"); return false; }
        bool ok = Transfer(new[] { host }, () =>
        {
            configuration.Revision++;
            host.Router.Configuration = configuration;
            host.NextAdvance = 0;
        });
        if (!ok) SetStatus(host, "save-paused");
        return ok;
    }
    private bool SearchStep() => --searchBudget >= 0;
    private bool Eligible(BlockEntityPneumaticTransport h, string owner) => h.CanWrite && h.Node.Valid && h.Air > 0 && Loaded(h.Pos) && Access(owner, h.Pos);
    private bool TryGraphOrder(BlockEntityPneumaticTransport receiver, PneumaticInventory destination, BlockFacing face, bool outlet)
    {
        var state = receiver.State;
        if (!Access(state.Owner, receiver.Pos) || !InventoryAccess(state.Owner, destination))
        { SetStatus(receiver, "protected-route"); return false; }
        var graph = new PneumaticRouteGraph(hosts, h => Eligible(h, state.Owner), SearchStep);
        (BlockEntityPneumaticTransport Sender, PneumaticInventory Source, ItemSlot Slot, int Count,
            BlockEntityPneumaticTransport[] Route, int Cost)? best = null;
        var suppliers = graph.Suppliers(receiver);
        int examined = 0, offset = receiver.RouterSearchCursor;
        int supplierStart = suppliers.Length == 0 ? 0 : receiver.RouterSupplierCursor % suppliers.Length;
        foreach (var offer in suppliers.Skip(supplierStart).Concat(suppliers.Take(supplierStart)))
        {
            var sender = offer.Host;
            if (sender.State.Cargo != null || sender.State.Returning) continue;
            var source = sender.Chest;
            if (source == null || source.Inventory.TakeLocked || source.Receipt.Instance == destination.Receipt.Instance ||
                !InventoryAccess(sender.State.Owner, source) || !InventoryAccess(state.Owner, source)) continue;
            for (int slotOffset = 0; slotOffset < source.Inventory.Count && !graph.Exhausted; slotOffset++)
            {
                int index = (slotOffset + offset) % source.Inventory.Count;
                if (++examined > 64) break;
                var slot = source.Inventory[index];
                if (slot.Empty || !slot.CanTake() || !PneumaticInventory.Supported(slot.Itemstack)) continue;
                int count = Math.Min(8, Math.Min(slot.StackSize, destination.Capacity(api!.World, slot.Itemstack)));
                count = LimitStockOrder(receiver, slot.Itemstack, count);
                if (count == 0) continue;
                var route = graph.Find(sender, receiver, slot.Itemstack);
                if (route == null || best != null && (route.Value.Cost > best.Value.Cost || route.Value.Cost == best.Value.Cost &&
                    ComparePosition(sender.Position, best.Value.Sender.Position) >= 0)) continue;
                best = (sender, source, slot, count, route.Value.Hosts, route.Value.Cost);
            }
            if (graph.Exhausted || examined > 64) break;
        }
        receiver.RouterSearchCursor = (offset + 1) % 16;
        receiver.RouterSupplierCursor = supplierStart + 1;
        if (best == null)
        { if (graph.Exhausted || examined > 64) SetStatus(receiver, "search-limit"); return false; }
        var chosen = best.Value;
        string[] routeKeys = chosen.Route.Select(h => Encode(h.Pos)).ToArray();
        string parcel = Guid.NewGuid().ToString("N"), receipt = Guid.NewGuid().ToString("N");
        var routers = chosen.Route.Where(h => h.Kind == PneumaticLineKind.Router).ToArray();
        bool ok = Transfer(new BlockEntity[] { chosen.Source.Chest, destination.Chest, chosen.Sender, receiver }.Concat(routers), () =>
        {
            var cargo = new DummySlot();
            if (chosen.Slot.TryPutInto(api!.World, cargo, chosen.Count) != chosen.Count) throw new InvalidOperationException("Source changed during router reservation.");
            var s = chosen.Sender.State;
            s.Cargo = cargo.Itemstack; s.Parcel = parcel; s.Destination = state.Instance; s.Route = routeKeys; s.RouteIndex = 0;
            s.DeliveryOutlet = outlet; s.DeliveryFace = face; s.DeliveryInventory = destination.Receipt.Instance;
            s.Loading = true; s.Progress = 0; s.Receipt = receipt; chosen.Sender.NextAdvance = 0;
            chosen.Source.Receipt.Receipt = receipt;
            state.Outstanding = parcel; state.OrderRoute = routeKeys; state.Receipt = receipt;
            if (receiver.IsSmartReceiver) receiver.Stockkeeper.BeginOrder(parcel, cargo.Itemstack!);
            for (int i = 1; i < chosen.Route.Length - 1; i++)
                if (chosen.Route[i].Kind == PneumaticLineKind.Router)
                {
                    var r = chosen.Route[i].Router;
                    int output = r.Port(PneumaticRouteGraph.Between(chosen.Route[i].Position, chosen.Route[i + 1].Position)!);
                    if (r.Configuration.Policy != "priority") r.Cursor = output % 4;
                }
        });
        SetStatus(receiver, ok ? "ordered" : "save-paused");
        if (ok)
        {
            receiver.PreferOutlet = !outlet;
        }
        return true;
    }

    private static int ComparePosition(PneumaticPosition a, PneumaticPosition b) =>
        (a.Dimension, a.X, a.Y, a.Z).CompareTo((b.Dimension, b.X, b.Y, b.Z));

    private void PrepareRouters(BlockEntityPneumaticTransport[] active, double seconds)
    {
        var expected = new Dictionary<string, (BlockEntityPneumaticTransport Host, int Index, int Distance)>();
        foreach (var h in active.Where(h => h.State.Cargo != null).Take(64))
            for (int i = h.State.RouteIndex + 1; i < h.State.Route.Length - 1; i++)
            {
                string key = h.State.Route[i]; int distance = i - h.State.RouteIndex;
                if (!expected.TryGetValue(key, out var old) || distance < old.Distance || distance == old.Distance &&
                    StringComparer.Ordinal.Compare(h.State.Parcel, old.Host.State.Parcel) < 0) expected[key] = (h, i, distance);
            }
        foreach (var router in active.Where(h => h.Kind == PneumaticLineKind.Router && h.State.Cargo == null))
        {
            if (!expected.TryGetValue(Encode(router.Pos), out var arrival)) { router.Router.ExpectedParcel = ""; continue; }
            var incoming = arrival.Host; int index = arrival.Index;
            if (index <= 0 || index >= incoming.State.Route.Length - 1) continue;
            int input = router.Router.Port(PneumaticRouteGraph.Between(router.Position, Position(ParsePosition(incoming.State.Route[index - 1])!))!);
            int output = router.Router.Port(PneumaticRouteGraph.Between(router.Position, Position(ParsePosition(incoming.State.Route[index + 1])!))!);
            if (input == 0 || output == 0) continue;
            if (router.Router.ReadyInput != input)
            { router.Router.RestAngle = PneumaticRouterMotion.Pose(router).Angle; router.Router.Preparation = 0; }
            router.Router.ReadyInput = input; router.Router.ReadyOutput = output; router.Router.ExpectedParcel = incoming.State.Parcel;
            if (router.Air > 0) router.Router.Preparation = Math.Min(1, router.Router.Preparation + seconds / PneumaticRouterState.PrepareSeconds);
        }
    }

    private bool RouterHandoffAllowed(BlockEntityPneumaticTransport host, BlockEntityPneumaticTransport next, BlockFacing exit)
    {
        var s = host.State;
        if (host.Kind == PneumaticLineKind.Router && !host.Router.Allows(s.Cargo!, host.Router.TransitInput, host.Router.Port(exit), host.RouterOutputs, true))
        { SetStatus(host, "filtered"); return false; }
        if (next.Kind != PneumaticLineKind.Router) return true;
        int entry = next.Router.Port(exit.Opposite);
        // Catch a committed parcel even if its old outgoing path has vanished.
        // The carriage can retain it and repair its route without losing cargo.
        if (!next.RouterPorts("input").Contains(exit.Opposite))
        { SetStatus(host, "filtered"); return false; }
        if (next.State.Cargo != null) return true;
        if (next.Air <= 0) { SetStatus(host, "no-air"); return false; }
        if (next.Router.ExpectedParcel != s.Parcel || next.Router.ReadyInput != entry || next.Router.Preparation < 1)
        { SetStatus(host, "router-preparing"); return false; }
        return true;
    }
    private static void AdmitRouter(BlockEntityPneumaticTransport next, PneumaticState previous, BlockFacing exit)
    {
        next.Router.TransitInput = next.Router.Port(exit.Opposite);
        var outgoing = previous.RouteIndex + 2 < previous.Route.Length ? PneumaticRouteGraph.Between(next.Position,
            Position(ParsePosition(previous.Route[previous.RouteIndex + 2])!)) : exit;
        next.Router.TransitOutput = outgoing == null ? 0 : next.Router.Port(outgoing);
        if (next.Router.TransitOutput == 0) next.Router.TransitOutput = next.Router.TransitInput % 4 + 1;
        next.Router.Lost = false; next.Router.NextRouteCheck = 0;
        next.State.Input = exit.Opposite; next.State.Output = next.Router.Face(next.Router.TransitOutput);
    }
    private static void ResetRouter(BlockEntityPneumaticTransport host)
    {
        host.Router.RestAngle = (host.Router.TransitOutput - 1) * 90;
        host.Router.Preparation = 0; host.Router.ExpectedParcel = "";
        host.Router.Lost = false; host.Router.NextRouteCheck = 0;
    }

    private void CheckRouterRoute(BlockEntityPneumaticTransport host)
    {
        var s = host.State; var r = host.Router;
        if (s.Cargo == null || r.NextRouteCheck > api!.World.ElapsedMilliseconds) return;
        r.NextRouteCheck = api.World.ElapsedMilliseconds + 1000;
        var receiver = hosts.Values.FirstOrDefault(h => h.State.Instance == s.Destination && Loaded(h.Pos) && h.CanWrite);
        var inventory = receiver == null ? null : PneumaticInventory.At(api.World, receiver.Pos.AddCopy(s.DeliveryFace ?? receiver.State.InventoryFace));
        bool destination = receiver != null && inventory != null && (s.DeliveryInventory == "" || inventory.Receipt.Instance == s.DeliveryInventory) &&
            (s.DeliveryFace == null || s.DeliveryFace == (s.DeliveryOutlet ? receiver.State.Output : receiver.State.InventoryFace));
        bool Physical(BlockEntityPneumaticTransport h) => h.CanWrite && h.Node.Valid && Loaded(h.Pos) && Access(s.Owner, h.Pos);
        bool valid = destination && Physical(receiver!) && InventoryAccess(s.Owner, inventory!);
        var current = host; var input = r.Face(r.TransitInput);
        for (int i = s.RouteIndex + 1; valid && i < s.Route.Length; i++)
        {
            if (!SearchStep()) return; // Budget exhaustion is not proof of a lost route.
            var position = Position(ParsePosition(s.Route[i])!);
            var exit = PneumaticRouteGraph.Between(current.Position, position);
            valid = hosts.TryGetValue(position, out var next) && Physical(next) && exit != null &&
                current.Node.Outlets.Contains(exit) && next.Node.Inlets.Contains(exit.Opposite) &&
                (current.Kind != PneumaticLineKind.Router || current.Router.Allows(s.Cargo, current.Router.Port(input),
                    current.Router.Port(exit), current.RouterOutputs, true));
            if (valid) { current = next!; input = exit!.Opposite; }
        }
        valid &= current == receiver;
        if (valid && !r.Lost) return;
        var graph = new PneumaticRouteGraph(hosts, Physical, SearchStep);
        var route = destination && Physical(receiver!) && InventoryAccess(s.Owner, inventory!) ?
            graph.Find(host, receiver!, s.Cargo, r.Face(r.TransitInput), committed: true) : null;
        if (graph.Exhausted)
        {
            // Hold uncertain cargo while the bounded search retries. Never
            // launch down an invalid old route because the search ran out.
            if (!r.Lost) Transfer(new[] { host }, () => { r.Lost = true; s.Progress = Math.Min(s.Progress, PneumaticRouterMotion.CloseEnd); });
            return;
        }
        if (route == null)
        {
            if (!r.Lost) Transfer(new[] { host }, () => { r.Lost = true; s.Progress = Math.Min(s.Progress, PneumaticRouterMotion.CloseEnd); });
            SetStatus(host, "lost"); return;
        }
        string[] keys = s.Route.Take(s.RouteIndex).Concat(route.Value.Hosts.Select(h => Encode(h.Pos))).ToArray();
        Transfer(new[] { host, receiver! }, () =>
        {
            s.Route = keys; receiver!.State.OrderRoute = keys;
            r.TransitOutput = r.Port(PneumaticRouteGraph.Between(host.Position, route.Value.Hosts[1].Position)!);
            s.Output = r.Face(r.TransitOutput); s.Progress = Math.Min(s.Progress, PneumaticRouterMotion.CloseEnd); r.Lost = false; host.NextAdvance = 0;
        });
    }
}
