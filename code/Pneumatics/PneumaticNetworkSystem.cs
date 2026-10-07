using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using Gearwright.Audio;

namespace Gearwright.Pneumatics;

/// <summary>Bounded, receiver-ordered direct lines; no junction discovery or forced chunk loads.</summary>
public sealed partial class PneumaticNetworkSystem : ModSystem
{
    private readonly Dictionary<PneumaticPosition, BlockEntityPneumaticTransport> hosts = new();
    private readonly Dictionary<PneumaticPosition, BlockEntityPneumaticAirIntake> intakes = new();
    private ICoreServerAPI? api;
    private Harmony? patches;
    private long listener;
    private IPneumaticTransferStore? store;
    private int fairCursor;
    private long nextOrders;
    private bool adapterUnavailable;
    internal static PneumaticPosition Position(BlockPos p) => new(p.dimension, p.X, p.Y, p.Z);
    internal static string Encode(BlockPos p) => FormattableString.Invariant($"{p.dimension},{p.X},{p.Y},{p.Z}");
    internal static BlockPos? ParsePosition(string value)
    {
        string[] parts = value.Split(','); var n = new int[4];
        if (parts.Length != 4) return null;
        for (int i = 0; i < 4; i++) if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return null;
        return new BlockPos(n[1], n[2], n[3], n[0]);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.api = api;
        patches = new Harmony("gearwright.pneumatic-transfers");
        PneumaticTransferStore.Install(patches);
        listener = api.Event.RegisterGameTickListener(Tick, 100);
    }
    internal void Register(BlockEntityPneumaticTransport host) => hosts[host.Position] = host;
    internal void Register(BlockEntityPneumaticAirIntake intake) => intakes[Position(intake.Pos)] = intake;
    internal void Unregister(BlockEntityPneumaticTransport host) => hosts.Remove(host.Position);
    internal void Unregister(BlockEntityPneumaticAirIntake intake) => intakes.Remove(Position(intake.Pos));
    public override void Dispose()
    {
        if (api != null) api.Event.UnregisterGameTickListener(listener);
        patches?.UnpatchAll("gearwright.pneumatic-transfers");
        PneumaticTransferStore.Reset(); hosts.Clear(); intakes.Clear();
    }

    private bool Loaded(BlockPos p) => api!.World.BlockAccessor.GetChunkAtBlockPos(p) != null;
    internal bool Access(string owner, BlockPos p)
    {
        if (api == null || !Loaded(p)) return false;
        var player = api.World.PlayerByUid(owner);
        return player != null && api.World.Claims.TestAccess(player, p, EnumBlockAccessFlags.Use) == EnumWorldAccessResponse.Granted;
    }

    private bool InventoryAccess(string owner, PneumaticInventory inventory)
    {
        if (!Access(owner, inventory.Chest.Pos)) return false;
        var player = api!.World.PlayerByUid(owner);
        var locks = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        return player != null && locks != null && !locks.IsLockedForInteract(inventory.Chest.Pos, player);
    }

    private bool Transfer(IEnumerable<BlockEntity> participants, Action apply, Action? extraRollback = null)
    {
        BlockEntity[] entities = participants.Distinct().ToArray();
        if (store?.Ready != true) return false;
        var before = entities.Select(e => { var t = new TreeAttribute(); e.ToTreeAttributes(t); return t; }).ToArray();
        bool ok = store.Commit(entities.Select(e => e.Pos).ToArray(), apply, () =>
        {
            for (int i = 0; i < entities.Length; i++) entities[i].FromTreeAttributes(before[i], api!.World);
            extraRollback?.Invoke();
        });
        foreach (var e in entities) e.MarkDirty(false);
        return ok;
    }

    private void Tick(float dt)
    {
        if (api == null || hosts.Count + intakes.Count == 0) return;
        if (hosts.Count + intakes.Count > PneumaticAir.MaximumNodes)
        { foreach (var h in hosts.Values) SetStatus(h, "network-limit"); return; }
        if (adapterUnavailable) { foreach (var h in hosts.Values) SetStatus(h, "save-paused"); return; }
        if (store == null)
        {
            try { store = new PneumaticTransferStore(api); }
            catch (Exception error)
            {
                api.Logger.Error("[Gearwright] Pneumatic save adapter unavailable; extraction disabled: {0}", error);
                adapterUnavailable = true;
                foreach (var h in hosts.Values) SetStatus(h, "save-paused");
                return;
            }
        }
        if (!store.Ready) { foreach (var h in hosts.Values) SetStatus(h, "save-paused"); return; }
        // No extrapolation after hitches. One common bounded interval per tick.
        const double seconds = .1;
        var active = hosts.Values.Where(h => Loaded(h.Pos) && h.Node.Valid && h.CanWrite).ToArray();
        searchBudget = 32768;
        foreach (var router in active.Where(h => h.Kind == PneumaticLineKind.Router && h.State.Cargo != null).Take(64)) CheckRouterRoute(router);
        PrepareRouters(active, seconds);
        var roots = intakes.Values.Where(i => Loaded(i.Pos) && i.CanWriteState).ToArray();
        var topology = active.Select(h => h.Node).Concat(roots.Select(i =>
            new PneumaticLineNode(Position(i.Pos), PneumaticLineKind.Intake, null, i.Outlet))).ToArray();
        var demand = new Dictionary<PneumaticPosition, double>();
        int moving = 0;
        foreach (var h in active)
        {
            if (moving >= 64) break;
            if (h.State.Returning)
            { demand[h.Position] = 2 * Math.Min(seconds / 1.6, 1 - h.State.ReturnProgress); moving++; }
            else if (h.State.Cargo != null && Access(h.State.Owner, h.Pos))
            {
                double end = h.Kind == PneumaticLineKind.Router && h.Router.Lost ? PneumaticRouterMotion.CloseEnd : h.State.HandoffProgress;
                if (h.State.Progress < end)
                {
                    double duration = h.Kind == PneumaticLineKind.Router ? PneumaticRouterState.TransitSeconds : h.State.Loading || h.IsReceiving ? 1.6 : .5;
                    demand[h.Position] = 2 * Math.Min(seconds / duration, end - h.State.Progress); moving++;
                }
            }
        }
        var flow = PneumaticLineFlow.Solve(topology,
            roots.Select(i => new PneumaticSupply(Position(i.Pos), i.ReleaseAir(seconds))).ToArray(), seconds, demand);
        if (flow.Status != "ok") return;
        foreach (var h in active)
        {
            bool supplied = flow.Sections.TryGetValue(h.Position, out var f) && f.Received > h.Node.LossPerSecond * seconds;
            h.Air = supplied ? f.Received / seconds : 0;
            if (h.IsSmartReceiver && supplied && Access(h.State.Owner, h.Pos)) h.Stockkeeper.AdvancePrint(seconds);
            if (h.Kind == PneumaticLineKind.Router) h.Router.SupplyPort = f.SelectedInput == null ? 0 : h.Router.Port(f.SelectedInput);
            if (h.State.Returning)
            {
                h.State.ReturnProgress = Math.Min(1, h.State.ReturnProgress + f.Movement / 2);
                if (h.State.ReturnProgress >= 1 - 1e-8) { h.State.Returning = false; h.State.ReturnProgress = 0; }
            }
            else if (h.State.Cargo != null)
            {
                h.State.Progress += f.Movement / 2;
            }
            if (!supplied || h.NextAdvance <= api.World.ElapsedMilliseconds)
                SetStatus(h, supplied ? h.State.Cargo == null ? "idle" : "moving" : "no-air");
            if (h.Kind == PneumaticLineKind.Router && h.State.Cargo != null && h.Router.Lost) SetStatus(h, "lost");
        }
        // Snapshot parcel IDs before moving, then downstream first. A parcel
        // never gets a second host transition in the same interval.
        foreach (var h in active.Where(h => h.State.Cargo != null).OrderByDescending(h => h.State.RouteIndex).Take(64))
        {
            if (h.Kind == PneumaticLineKind.Router && h.Router.Lost) continue;
            if (h.Air <= 0 || h.NextAdvance > api.World.ElapsedMilliseconds || !Access(h.State.Owner, h.Pos)) continue;
            double end = h.State.HandoffProgress;
            if (h.State.Progress >= end - 1e-8)
            {
                bool advanced = Advance(h);
                h.NextAdvance = advanced ? 0 : api.World.ElapsedMilliseconds + (h.Status == "router-preparing" ? 100 : 1000);
                if (advanced && h.State.Cargo == null)
                    foreach (var input in h.Node.Inlets)
                        if (hosts.TryGetValue(h.Position.Offset(input), out var behind) && behind.Node.Outlets.Contains(input.Opposite) && behind.Status == "occupied")
                            behind.NextAdvance = 0;
            }
        }
        // Idle/blocked order scans back off to once per second.
        if (api.World.ElapsedMilliseconds >= nextOrders)
        {
            nextOrders = api.World.ElapsedMilliseconds + 1000;
            var receivers = active.Where(h => h.Kind == PneumaticLineKind.InlineReceiver || h.OutletChest != null || h.State.Outstanding != "")
                .OrderBy(h => h.Position.Dimension).ThenBy(h => h.Position.X).ThenBy(h => h.Position.Y).ThenBy(h => h.Position.Z).ToArray();
            for (int i = 0; i < Math.Min(32, receivers.Length); i++)
                Order(receivers[(fairCursor + i) % receivers.Length]);
            if (receivers.Length > 0) fairCursor = (fairCursor + 1) % receivers.Length;
        }
        foreach (var h in active) h.MarkDirty(false);
    }

    private static void SetStatus(BlockEntityPneumaticTransport h, string status) { h.Status = status; h.MarkDirty(false); }

    private void Order(BlockEntityPneumaticTransport receiver)
    {
        var state = receiver.State;
        if (receiver.IsSmartReceiver) RefreshStockkeeper(receiver);
        if (state.Outstanding != "")
        {
            // The frozen route contains every possible host. A missing or
            // unloaded chunk never counts as proof of delivery/destruction.
            if (state.OrderRoute.Length == 0 || state.OrderRoute.Any(p => !Loaded(ParsePosition(p)!))) return;
            if (state.OrderRoute.Any(p => api!.World.BlockAccessor.GetBlockEntity(ParsePosition(p)!) is
                BlockEntityPneumaticTransport h && (!h.State.Writable || h.State.Parcel == state.Outstanding))) return;
            Transfer(new[] { receiver }, () => { state.Outstanding = ""; state.OrderRoute = Array.Empty<string>(); });
        }
        if (state.Outstanding != "" || state.Cargo != null || state.Returning || receiver.Air <= 0 ||
            receiver.IsSmartReceiver && receiver.Stockkeeper.Printing || hosts.Values.Count(h => h.State.Cargo != null) >= 64) return;
        var destinations = new List<(PneumaticInventory Inventory, BlockFacing Face, bool Outlet)>();
        if (receiver.Kind == PneumaticLineKind.InlineReceiver && receiver.Chest is { } branch)
            destinations.Add((branch, state.InventoryFace, false));
        if (!receiver.IsSmartReceiver && receiver.OutletChest is { } outlet) destinations.Add((outlet, state.Output, true));
        if (destinations.Count == 0) { SetStatus(receiver, "no-inventory"); return; }
        // Alternate between branch and mainline outlet; a full branch does not
        // suppress a usable terminal inventory on the same physical machine.
        if (receiver.PreferOutlet) destinations.Reverse();
        SetStatus(receiver, "no-offer");
        foreach (var destination in destinations)
            if (TryOrder(receiver, destination.Inventory, destination.Face, destination.Outlet)) return;
    }

    private bool TryOrder(BlockEntityPneumaticTransport receiver, PneumaticInventory destination, BlockFacing face, bool outlet)
    {
        if (hosts.Values.Any(h => h.Kind == PneumaticLineKind.Router)) return TryGraphOrder(receiver, destination, face, outlet);
        var state = receiver.State;
        if (!Access(state.Owner, receiver.Pos) || !InventoryAccess(state.Owner, destination))
        { SetStatus(receiver, "protected-route"); return false; }
        var path = new List<BlockEntityPneumaticTransport> { receiver };
        var visited = new HashSet<PneumaticPosition> { receiver.Position };
        var current = receiver;
        for (int distance = 0; distance < PneumaticAir.MaximumNodes; distance++)
        {
            var sender = current;
            if (distance > 0)
            {
                var previous = current.Position.Offset(current.State.Input);
                if (!hosts.TryGetValue(previous, out sender) || !visited.Add(previous) || !sender.State.Writable ||
                    sender.State.Output != current.State.Input.Opposite || sender.Air <= 0 || !Access(state.Owner, sender.Pos)) break;
                path.Add(sender); current = sender;
            }
            if (sender.Kind != PneumaticLineKind.Sender || sender.State.Cargo != null || sender.State.Returning) continue;
            var source = sender.Chest;
            if (source == null || source.Inventory.TakeLocked || source.Receipt.Instance == destination.Receipt.Instance ||
                !InventoryAccess(sender.State.Owner, source) || !InventoryAccess(state.Owner, source)) continue;
            for (int slotIndex = 0; slotIndex < source.Inventory.Count; slotIndex++)
            {
                var sourceSlot = source.Inventory[slotIndex];
                if (sourceSlot.Empty || !sourceSlot.CanTake() || !PneumaticInventory.Supported(sourceSlot.Itemstack)) continue;
                int count = Math.Min(8, Math.Min(sourceSlot.StackSize, destination.Capacity(api!.World, sourceSlot.Itemstack)));
                count = LimitStockOrder(receiver, sourceSlot.Itemstack, count);
                if (count <= 0) continue;
                var route = path.AsEnumerable().Reverse().Select(h => Encode(h.Pos)).ToArray();
                string parcel = Guid.NewGuid().ToString("N"), receipt = Guid.NewGuid().ToString("N");
                // Persist the destination's identity in the same reservation,
                // including old chests that have never saved this additive field.
                bool ok = Transfer(new BlockEntity[] { source.Chest, destination.Chest, sender, receiver }, () =>
                {
                    var cargo = new DummySlot();
                    int accepted = sourceSlot.TryPutInto(api.World, cargo, count);
                    if (accepted != count) throw new InvalidOperationException("Source changed during pneumatic reservation.");
                    sender.State.Cargo = cargo.Itemstack; sender.State.Parcel = parcel;
                    sender.State.Destination = state.Instance; sender.State.Route = route; sender.State.RouteIndex = 0;
                    sender.State.DeliveryOutlet = outlet; sender.State.DeliveryFace = face;
                    sender.State.DeliveryInventory = destination.Receipt.Instance;
                    sender.State.Loading = true; sender.State.Progress = 0; sender.State.Receipt = receipt;
                    sender.NextAdvance = 0;
                    source.Receipt.Receipt = receipt;
                    state.Outstanding = parcel; state.OrderRoute = route; state.Receipt = receipt;
                    if (receiver.IsSmartReceiver) receiver.Stockkeeper.BeginOrder(parcel, cargo.Itemstack!);
                });
                SetStatus(receiver, ok ? "ordered" : "save-paused");
                if (ok)
                {
                    receiver.PreferOutlet = !outlet;
                }
                return true;
            }
        }
        return false;
    }

    private bool Advance(BlockEntityPneumaticTransport host)
    {
        var s = host.State;
        bool last = s.Route.Length == 0 || s.RouteIndex == s.Route.Length - 1;
        if ((s.DeliveryOutlet || host.Kind == PneumaticLineKind.InlineReceiver) &&
            (s.Destination == host.State.Instance || s.Destination == "" && last))
        {
            var face = s.DeliveryFace ?? s.InventoryFace;
            if (face != (s.DeliveryOutlet ? s.Output : s.InventoryFace)) { SetStatus(host, "route-broken"); return false; }
            var target = PneumaticInventory.At(api!.World, host.Pos.AddCopy(face));
            if (target == null) { SetStatus(host, "no-inventory"); return false; }
            if (s.DeliveryInventory != "" && s.DeliveryInventory != target.Receipt.Instance)
            { SetStatus(host, "inventory-changed"); return false; }
            if (!InventoryAccess(s.Owner, target)) { SetStatus(host, "protected-route"); return false; }
            if (target.Capacity(api!.World, s.Cargo!) == 0) { SetStatus(host, "destination-full"); return false; }
            bool audibleOutlet = s.DeliveryOutlet && !s.Loading;
            string receipt = Guid.NewGuid().ToString("N");
            bool delivered = Transfer(new BlockEntity[] { host, target.Chest }, () =>
            {
                var slot = new DummySlot(s.Cargo);
                target.Insert(api.World, slot); s.Cargo = slot.Itemstack;
                target.Receipt.Receipt = receipt; s.Receipt = receipt;
                if (s.Cargo == null)
                {
                    if (s.Outstanding == s.Parcel) { s.Outstanding = ""; s.OrderRoute = Array.Empty<string>(); }
                    if (s.Loading) { s.Returning = true; s.ReturnProgress = s.Progress; }
                    s.ClearCargo();
                }
            });
            if (delivered && s.Cargo == null && audibleOutlet)
                MachineSoundPolicy.Information(api.World, host.Pos, "outlet-arrival");
            return delivered;
        }
        if (last) { SetStatus(host, "route-broken"); return false; }
        var nextPos = ParsePosition(s.Route[s.RouteIndex + 1])!;
        var exit = PneumaticRouteGraph.Between(host.Position, Position(nextPos));
        if (!hosts.TryGetValue(Position(nextPos), out var next) || !next.CanWrite || !Loaded(nextPos) || exit == null ||
            !host.Node.Outlets.Contains(exit) || !next.Node.Inlets.Contains(exit.Opposite) ||
            !Access(s.Owner, nextPos)) { SetStatus(host, "route-broken"); return false; }
        if (!RouterHandoffAllowed(host, next, exit)) return false;
        if (next.State.Cargo != null || next.State.Returning) { SetStatus(host, "occupied"); return false; }
        string transfer = Guid.NewGuid().ToString("N");
        return Transfer(new BlockEntity[] { host, next }, () =>
        {
            if (next.Kind == PneumaticLineKind.Router) AdmitRouter(next, s, exit);
            next.State.Cargo = s.Cargo; next.State.Parcel = s.Parcel; next.State.Destination = s.Destination;
            next.State.DeliveryOutlet = s.DeliveryOutlet; next.State.DeliveryFace = s.DeliveryFace;
            next.State.DeliveryInventory = s.DeliveryInventory;
            next.State.Route = s.Route; next.State.RouteIndex = s.RouteIndex + 1;
            next.State.Progress = 0; next.State.Loading = false; next.State.Receipt = transfer;
            next.NextAdvance = 0;
            if (s.Loading) { s.Returning = true; s.ReturnProgress = s.Progress; }
            if (host.Kind == PneumaticLineKind.Router) ResetRouter(host);
            s.ClearCargo(); s.Receipt = transfer;
        });
    }

    internal bool Drop(BlockEntityPneumaticTransport host)
    {
        if (host.State.Cargo == null) return true;
        if (!host.CanWrite || store?.Ready != true) return false;
        Vintagestory.API.Common.Entities.Entity? dropped = null;
        return Transfer(new[] { host }, () =>
        {
            var stack = host.State.Cargo!;
            host.State.ClearCargo(); host.State.Receipt = Guid.NewGuid().ToString("N");
            dropped = api!.World.SpawnItemEntity(stack, host.Pos.ToVec3d().Add(.5, .5, .5), new Vec3d());
            if (dropped == null) throw new InvalidOperationException("The cargo drop could not be created.");
        }, () => { if (dropped != null) api!.World.DespawnEntity(dropped, new EntityDespawnData { Reason = EnumDespawnReason.Removed }); });
    }
}
