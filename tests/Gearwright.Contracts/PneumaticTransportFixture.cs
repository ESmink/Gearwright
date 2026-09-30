using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Gearwright.Pneumatics;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticTransportFixture
{
    internal sealed class MemoryStore : IPneumaticTransferStore
    {
        public bool Ready => true;
        public bool Fail;
        public int Commits;
        public BlockPos[] LastPositions = Array.Empty<BlockPos>();
        public bool Commit(IReadOnlyList<BlockPos> positions, Action apply, Action rollback)
        {
            try { apply(); if (Fail) { Fail = false; throw new IOException("injected transaction abort"); } LastPositions = positions.ToArray(); Commits++; return true; }
            catch { rollback(); return false; }
        }
    }

    internal sealed class Rig : IDisposable
    {
        internal readonly Dictionary<BlockPos, BlockEntity> Entities = new();
        internal readonly HashSet<BlockPos> Unloaded = new(), Denied = new(), Locked = new();
        internal readonly List<ItemStack> Drops = new();
        internal readonly List<BlockEntityPneumaticTransport> Hosts = new();
        internal readonly MemoryStore Store = new();
        internal readonly ModSystemBlockReinforcement Reinforcement = new();
        internal readonly PneumaticNetworkSystem System = new();
        internal readonly ICoreServerAPI Api;
        internal readonly IServerWorldAccessor World;
        internal readonly Item Item = new() { Code = new("game:stone-granite"), MaxStackSize = 64 };
        internal BlockEntityPneumaticAirIntake Intake;
        internal BlockEntityPneumaticTransport Sender, Receiver;
        internal BlockEntityGenericTypedContainer Source, Target;
        private long now;
        internal Rig(bool elbows = false, int tubes = 2)
        {
            var logger = Stub.Create<ILogger>((_, _) => null);
            var chunk = Stub.Create<IWorldChunk>((m, _) => m.Name == "GetModdata" ? SerializerUtil.Serialize(
                Locked.ToDictionary(p => (int)AccessTools.Method(typeof(ModSystemBlockReinforcement), "toLocalIndex", new[] { typeof(BlockPos) })
                    .Invoke(Reinforcement, new object[] { p })!, _ => new BlockReinforcement { Locked = true, PlayerUID = "other-player" })) : null);
            var playerData = Stub.Create<IWorldPlayerData>((m, _) => m.Name == "get_CurrentGameMode" ? EnumGameMode.Survival : null);
            var player = Stub.Create<IPlayer>((m, _) => m.Name switch {
                "get_PlayerUID" => "builder", "get_WorldData" => playerData, _ => null });
            var claims = Stub.Create<ILandClaimAPI>((m, a) => m.Name == "TestAccess" ?
                Denied.Contains((BlockPos)a![1]!) ? (EnumWorldAccessResponse)99 : EnumWorldAccessResponse.Granted : null);
            var accessor = Stub.Create<IBlockAccessor>((m, a) => m.Name switch {
                "GetBlockEntity" => Entities.GetValueOrDefault((BlockPos)a![0]!),
                "GetBlock" => Entities.GetValueOrDefault((BlockPos)a![0]!)?.Block ?? new Block(),
                "GetChunkAtBlockPos" => Unloaded.Contains((BlockPos)a![0]!) ? null : chunk, _ => null });
            var calendar = Stub.Create<IGameCalendar>((_, _) => null);
            var inventoryNetwork = Stub.Create<IInventoryNetworkUtil>((_, _) => null);
            var registry = Stub.Create<IClassRegistryAPI>((m, _) => m.ReturnType == typeof(IInventoryNetworkUtil) ? inventoryNetwork : null);
            var loader = Stub.Create<IModLoader>((m, _) => m.ReturnType == typeof(ModSystemBlockReinforcement) ? Reinforcement : null);
            World = Stub.Create<IServerWorldAccessor>((m, a) => m.Name switch {
                "get_BlockAccessor" => accessor, "get_Claims" => claims, "PlayerByUid" => player,
                "get_Logger" => logger, "get_Calendar" => calendar, "get_ElapsedMilliseconds" => now, "get_ClassRegistry" => registry,
                "GetItem" => Item, "SpawnItemEntity" => AddDrop((ItemStack)a![0]!), _ => null });
            Api = Stub.Create<ICoreServerAPI>((m, _) => m.Name switch {
                "get_World" => World, "get_Logger" => logger, "get_Side" => EnumAppSide.Server, "get_ClassRegistry" => registry,
                "get_ModLoader" => loader, _ => null });
            Set(Reinforcement, "api", Api);
            Set(Item, "api", Api);
            Set(System, "api", Api); Set(System, "store", Store);
            Intake = new() { Api = Api, Pos = new(0, 2, 0) };
            Set(Intake, "Block", new Block { Code = new("gearwright:pneumatic-accumulator") });
            Intake.SetOutlet(BlockFacing.EAST); Entities[Intake.Pos] = Intake; System.Register(Intake);
            Sender = Host(1, 2, "sender");
            if (elbows)
            {
                Host(2, 2, "tube", BlockFacing.WEST, BlockFacing.UP);
                Host(2, 3, "tube", BlockFacing.DOWN, BlockFacing.UP);
                Host(2, 4, "tube", BlockFacing.DOWN, BlockFacing.EAST);
                Host(3, 4, "tube"); Host(4, 4, "tube"); Receiver = Host(5, 4, "receiver");
            }
            else { for (int x = 2; x < tubes + 2; x++) Host(x, 2, "tube"); Receiver = Host(tubes + 2, 2, "receiver"); }
            Source = Chest(Sender.Pos.DownCopy()); Target = Chest(Receiver.Pos.DownCopy());
        }
        private object? AddDrop(ItemStack stack) { Drops.Add(stack); return new EntityItem { Itemstack = stack }; }
        internal BlockEntityPneumaticTransport Host(int x, int y, string kind, BlockFacing? input = null, BlockFacing? output = null, int z = 0)
        {
            var h = new BlockEntityPneumaticTransport { Api = Api, Pos = new(x, y, z) };
            Set(h, "Block", new Block { Code = new("gearwright:pneumatic-" + kind) });
            h.State.Owner = "builder"; h.State.Input = input ?? BlockFacing.WEST; h.State.Output = output ?? BlockFacing.EAST;
            Entities[h.Pos] = h; Hosts.Add(h); System.Register(h); return h;
        }
        internal BlockEntityGenericTypedContainer Chest(BlockPos pos)
        {
            var chest = new BlockEntityGenericTypedContainer { Api = Api, Pos = pos, type = "normal-generic" };
            Set(chest, "Block", new Block { Code = new("game:chest-east") });
            Set(chest, "inventory", new InventoryGeneric(16, "chest-" + pos, Api));
            chest.Behaviors.Add(new BEBehaviorPneumaticChest(chest)); Entities[pos] = chest; return chest;
        }
        internal void Step(bool supply = true)
        {
            now += 100;
            if (supply) Intake.ReceiveAir(World, Intake.Pos, new(.2, .1, BlockFacing.EAST));
            AccessTools.Method(typeof(PneumaticNetworkSystem), "Tick").Invoke(System, new object[] { .1f });
        }
        internal int Total => Source.Inventory.Sum(s => s.StackSize) + Target.Inventory.Sum(s => s.StackSize) +
            Hosts.Sum(h => h.State.Cargo?.StackSize ?? 0) + Drops.Sum(s => s.StackSize);
        internal void Fill(int count = 64) { Source.Inventory[0].Itemstack = new ItemStack(Item, count); Source.Inventory[0].Itemstack!.Attributes.SetString("provenance", "original stack"); }
        internal void Reload()
        {
            foreach (var entity in Entities.Values)
            {
                var t = new TreeAttribute(); entity.ToTreeAttributes(t); entity.FromTreeAttributes(t, World);
            }
        }
        public void Dispose() { }
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (bool elbows in new[] { false, true })
        {
            using var rig = new Rig(elbows); rig.Fill();
            bool conserved = true, seenCargo = false, boundedHops = true;
            var previousHosts = new Dictionary<string, int>();
            for (int tick = 0; tick < 1200; tick++)
            {
                rig.Step(); conserved &= rig.Total == 64;
                foreach (var h in rig.Hosts.Where(h => h.State.Cargo != null))
                {
                    seenCargo = true;
                    if (previousHosts.TryGetValue(h.State.Parcel, out int previous)) boundedHops &= h.State.RouteIndex - previous <= 1;
                    previousHosts[h.State.Parcel] = h.State.RouteIndex;
                }
                if (tick % 11 == 0) rig.Reload();
            }
            check(conserved && seenCargo && boundedHops && rig.Target.Inventory.Sum(s => s.StackSize) == 64,
                "Runtime " + (elbows ? "vertical elbow" : "straight") + " rig delivers all 64 with one owner, one hop per tick and repeated native save/load");
            check(rig.Target.Inventory.Where(s => !s.Empty).All(s => s.Itemstack!.Attributes.GetString("provenance") == "original stack"),
                "Runtime cargo keeps actual stack attributes through native extraction and insertion");
        }
        using (var r = new Rig())
        {
            r.Fill(); r.System.Unregister(r.Receiver);
            for (int i = 0; i < 80; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 64 && r.Hosts.All(h => h.State.Cargo == null), "A stocked sender never extracts without a receiver order");
        }
        using (var r = new Rig())
        {
            r.Fill(); r.Denied.Add(r.Source.Pos);
            for (int i = 0; i < 30; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 64, "Current chest claims reject automation before extraction");
            r.Denied.Clear();
            for (int i = 0; i < 200 && r.Hosts.All(h => h.State.Cargo == null); i++) r.Step();
            var occupied = r.Hosts.First(h => h.State.Cargo != null);
            var before = occupied.State.Progress; r.Unloaded.Add(occupied.Pos);
            for (int i = 0; i < 30; i++) r.Step();
            check(r.Total == 64 && occupied.State.Progress == before, "Unloaded hosts hold cargo and prevent replacement orders");
            r.Unloaded.Clear();
            for (int i = 0; i < 6000; i++) r.Step(false);
            var positions = r.Hosts.Select(h => h.State.Progress).ToArray();
            for (int i = 0; i < 20; i++) r.Step(false);
            check(r.Total == 64 && positions.SequenceEqual(r.Hosts.Select(h => h.State.Progress)), "Exhausted accumulator air stops cargo at its exact progress");
        }
        using (var r = new Rig())
        {
            r.Fill();
            for (int i = 0; i < 200 && r.Hosts.All(h => h.State.Cargo == null); i++) r.Step();
            for (int i = 0; i < r.Target.Inventory.Count; i++) r.Target.Inventory[i].Itemstack = new ItemStack(r.Item, 64);
            int expected = r.Total;
            for (int i = 0; i < 150; i++) r.Step();
            check(r.Total == expected && r.Receiver.State.Cargo != null, "Unexpectedly full destination holds real cargo without overwriting inventory");
            check(r.Receiver.Status == "destination-full" && r.Receiver.NextAdvance > r.World.ElapsedMilliseconds,
                "A blocked receiver retains its reason and backs off between inventory retries");
            r.Target.Inventory[0].Itemstack = null; expected -= 64;
            for (int i = 0; i < 300; i++) r.Step();
            check(r.Total == expected && r.Receiver.State.Cargo == null, "Freeing destination capacity resumes native insertion");
        }
        using (var r = new Rig())
        {
            r.Fill(); r.Locked.Add(r.Source.Pos);
            for (int i = 0; i < 30; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 64, "Native reinforcement locks prevent extracting from another player's chest");
            r.Locked.Clear();
            for (int i = 0; i < 200 && r.Hosts.All(h => h.State.Cargo == null); i++) r.Step();
            check(r.Store.LastPositions.Contains(r.Target.Pos),
                "Reservation commits the destination identity with extraction, including previously unsaved chest metadata");
            r.Locked.Add(r.Target.Pos);
            for (int i = 0; i < 150; i++) r.Step();
            check(r.Total == 64 && r.Target.Inventory.All(s => s.Empty) && r.Receiver.State.Cargo != null,
                "Locking the destination after dispatch preserves the waiting cargo");
        }
        using (var r = new Rig())
        {
            r.Fill(); r.Store.Fail = true;
            for (int i = 0; i < 12; i++) r.Step();
            check(r.Total == 64, "An aborted extraction transaction restores native source and host state");
            var h = r.Hosts.FirstOrDefault(h => h.State.Cargo != null);
            for (int i = 0; h == null && i < 200; i++) { r.Step(); h = r.Hosts.FirstOrDefault(n => n.State.Cargo != null); }
            bool first = h != null && r.System.Drop(h), second = h != null && r.System.Drop(h);
            check(first && second && r.Drops.Count == 1 && r.Total == 64, "Repeated host removal callbacks drop the real stack once");
        }
        CongestionClearsPromptly(check);
        StateAndPose(check);
        AutomaticOutlets(check);
        using (var r = new Rig(tubes: 128))
        {
            r.Fill(8); bool conserved = true;
            for (int tick = 0; tick < 2400 && r.Target.Inventory.All(s => s.Empty); tick++)
            {
                r.Step(); conserved &= r.Total == 8;
                if (tick % 101 == 0) r.Reload();
            }
            check(conserved && r.Target.Inventory.Sum(s => s.StackSize) == 8,
                "One bellow supplies a real eight-item delivery across 128 tubes, including repeated save/reload");
        }
    }

    private static void StateAndPose(Action<bool, string> check)
    {
        using var r = new Rig();
        var fixture = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-host-schema1.json")));
        var original = new TreeAttribute(); original.SetInt("schemaVersion", fixture["schemaVersion"].AsInt());
        foreach (string key in new[] { "instance", "owner", "input", "output", "parcel", "future-field" }) original.SetString(key, fixture[key].AsString());
        original.SetDouble("progress", fixture["progress"].AsDouble());
        var roundtrip = PneumaticState.Read(original, r.World);
        check(roundtrip.Writable && roundtrip.InventoryFace == BlockFacing.DOWN && ((ITreeAttribute)roundtrip.Write()).GetInt("schemaVersion") == 3 &&
            ((ITreeAttribute)roundtrip.Write()).GetString("future-field") == "preserve", "Host schema one migrates with downward inventory and unknown fields intact");
        var v2 = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-host-schema2.json")));
        var old2 = original.Clone(); old2.SetInt("schemaVersion", v2["schemaVersion"].AsInt());
        old2.SetString("inventoryFace", v2["inventoryFace"].AsString());
        var current2 = PneumaticState.Read(PneumaticState.Read(old2, r.World).Write(), r.World);
        check(current2.Writable && current2.InventoryFace == BlockFacing.UP && !current2.DeliveryOutlet && current2.DeliveryFace == null,
            "Schema two migrates to three with its chosen branch and legacy delivery semantics intact");
        foreach (int version in new[] { -1, 0, 4, 99 })
        {
            var bad = original.Clone(); bad.SetInt("schemaVersion", version);
            var state = PneumaticState.Read(bad, r.World);
            check(!state.Writable && Bytes(bad).SequenceEqual(Bytes(state.Write())), "Unsupported host schema remains byte-identical and read-only");
        }
        var corrupt = original.Clone(); corrupt.SetDouble("progress", double.NaN);
        check(!PneumaticState.Read(corrupt, r.World).Writable, "Malformed progress cannot enter transport simulation");
        foreach (var input in BlockFacing.ALLFACES)
        foreach (var output in BlockFacing.ALLFACES.Where(f => f != input))
        {
            var matrix = PneumaticRenderer.Orientation(input, output);
            var localOutput = input.Opposite == output ? new Vec4f(1, .5f, .5f, 1) : new Vec4f(.5f, 1, .5f, 1);
            float[] result = Mat4f.MulWithVec4(matrix, new[] { localOutput.X, localOutput.Y, localOutput.Z, 1f });
            check(Math.Abs(result[0] - (.5 + output.Normali.X * .5)) < 1e-6 &&
                Math.Abs(result[1] - (.5 + output.Normali.Y * .5)) < 1e-6 &&
                Math.Abs(result[2] - (.5 + output.Normali.Z * .5)) < 1e-6,
                "Tube mesh and cargo ports agree for " + input.Code + " -> " + output.Code);
        }
        check(Math.Abs(PneumaticRenderer.ChargeFrame(.5) - 50) < 1e-4, "The accumulator scale reads actual stored reserve");
        foreach (var output in BlockFacing.ALLFACES)
        {
            var state = new PneumaticState { Output = output, Input = output.Opposite, DeliveryOutlet = true, Progress = 1 };
            state.Destination = state.Instance;
            var center = PneumaticRenderer.CargoPosition(state, PneumaticLineKind.Tube, false);
            double along = (center.X - .5) * output.Normali.X + (center.Y - .5) * output.Normali.Y + (center.Z - .5) * output.Normali.Z;
            check(along - .075 > (16.11 - 8) / 16, "Final outlet cargo is fully inside the dark sleeve on " + output.Code);
            state.Loading = true; state.Progress = state.HandoffProgress;
            center = PneumaticRenderer.CargoPosition(state, PneumaticLineKind.Sender, false);
            check(center.X * 16 - 1.2 > 16.11 && state.HandoffProgress == 240.0 / 360,
                "Direct sender delivery finishes its launch inside the dark outlet before the tray returns");
        }
        // Keep real cargo through the schema migration, including route/identity and unknown item attributes.
        var cargoTree = original.Clone();
        var cargo = new ItemStack(r.Item, 8); cargo.Attributes.SetString("provenance", "old cargo");
        cargoTree.SetItemstack("cargo", cargo); cargoTree.SetString("parcel", Guid.NewGuid().ToString("N"));
        cargoTree.SetDouble("progress", .42);
        cargoTree["route"] = new StringArrayAttribute(new[] { PneumaticNetworkSystem.Encode(r.Sender.Pos) });
        var migrated = PneumaticState.Read(cargoTree, r.World);
        var cargoReload = PneumaticState.Read(migrated.Write(), r.World);
        check(cargoReload.Writable && cargoReload.InventoryFace == BlockFacing.DOWN && cargoReload.Cargo?.StackSize == 8 &&
            cargoReload.Cargo.Attributes.GetString("provenance") == "old cargo" && cargoReload.Progress == .42 &&
            cargoReload.Parcel == cargoTree.GetString("parcel") && cargoReload.Route.Length == 1,
            "Schema one in-flight cargo migrates and reloads with its stack, identity, progress and route intact");
        cargoTree.SetInt("schemaVersion", 2); cargoTree.SetString("inventoryFace", "up");
        var v2Cargo = PneumaticState.Read(PneumaticState.Read(cargoTree, r.World).Write(), r.World);
        check(v2Cargo.Writable && v2Cargo.InventoryFace == BlockFacing.UP && !v2Cargo.DeliveryOutlet &&
            v2Cargo.DeliveryFace == null && v2Cargo.DeliveryInventory == "" && v2Cargo.Cargo?.StackSize == 8 &&
            v2Cargo.Parcel == cargoReload.Parcel && v2Cargo.Progress == .42,
            "Schema two in-flight cargo keeps its original receiver branch through migration and reload");
        foreach (var mutate in new Action<ITreeAttribute>[] {
            t => t.SetString("deliveryOutlet", "true"), t => t.SetInt("deliveryFace", 3),
            t => t.SetString("deliveryFace", "diagonal"), t => t.SetBool("deliveryOutlet", true),
            t => t.SetString("deliveryInventory", "not-an-instance"), t => t.SetInt("deliveryInventory", 4) })
        {
            var bad = (ITreeAttribute)v2Cargo.Write(); mutate(bad);
            var state = PneumaticState.Read(bad, r.World);
            check(!state.Writable && Bytes(bad).SequenceEqual(Bytes(state.Write())),
                "Malformed schema-three delivery data preserves the entire original cargo document");
        }
        var orientedHost = r.Host(10, 10, "sender");
        foreach (var output in BlockFacing.ALLFACES)
        foreach (var branch in BlockFacing.ALLFACES.Where(f => f.Axis != output.Axis))
        {
            orientedHost.State.Output = output; orientedHost.State.Input = output.Opposite; orientedHost.State.InventoryFace = branch;
            var chest = r.Chest(orientedHost.Pos.AddCopy(branch));
            var savedHost = new TreeAttribute(); orientedHost.ToTreeAttributes(savedHost); orientedHost.FromTreeAttributes(savedHost, r.World);
            check(orientedHost.Node.Valid && orientedHost.State.InventoryFace == branch && orientedHost.Chest?.Chest == chest,
                "Native host reload selects the actual " + branch.Code + " chest on a " + output.Code + " mainline");
        }
        var badBranch = migrated.Write() as ITreeAttribute;
        badBranch!.SetString("inventoryFace", "invalid-face");
        check(!PneumaticState.Read(badBranch, r.World).Writable && Bytes(badBranch).SequenceEqual(Bytes(PneumaticState.Read(badBranch, r.World).Write())),
            "Malformed schema-two inventory directions protect the entire original cargo document");
        var chestFixture = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-chest-schema1.json")));
        var receipt = new TreeAttribute(); receipt.SetInt("schemaVersion", chestFixture["schemaVersion"].AsInt());
        foreach (string key in new[] { "instance", "receipt", "future-field" }) receipt.SetString(key, chestFixture[key].AsString());
        const string chestKey = "gearwrightPneumaticChest";
        var parent = new TreeAttribute(); parent[chestKey] = receipt;
        var behavior = r.Source.GetBehavior<BEBehaviorPneumaticChest>();
        behavior.FromTreeAttributes(parent, r.World);
        var saved = new TreeAttribute(); behavior.ToTreeAttributes(saved);
        behavior.FromTreeAttributes(saved, r.World);
        check(behavior.Writable && behavior.Instance == receipt.GetString("instance") &&
            Bytes(receipt).SequenceEqual(Bytes(saved[chestKey])), "Chest receipt schema one reloads with identity and unknown fields intact");
        foreach (int version in new[] { 0, 2, 99 })
        {
            var bad = receipt.Clone(); bad.SetInt("schemaVersion", version); parent[chestKey] = bad;
            behavior.FromTreeAttributes(parent, r.World); behavior.ToTreeAttributes(saved);
            check(!behavior.Writable && Bytes(bad).SequenceEqual(Bytes(saved[chestKey])) && PneumaticInventory.At(r.World, r.Source.Pos) == null,
                "Unsupported chest receipt is read-only and excluded from transport");
        }
    }
    private static byte[] Bytes(IAttribute value) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); value.ToBytes(writer); return stream.ToArray(); }

    private static void CongestionClearsPromptly(Action<bool, string> check)
    {
        using var r = new Rig();
        for (int i = 0; i < 400; i++) r.Step();
        string[] route = r.Hosts.Select(h => PneumaticNetworkSystem.Encode(h.Pos)).ToArray();
        var upstream = r.Hosts[1]; var downstream = r.Hosts[2];
        foreach (var h in new[] { upstream, downstream })
        {
            h.State.Cargo = new ItemStack(r.Item, 8);
            h.State.Parcel = Guid.NewGuid().ToString("N");
            h.State.Destination = r.Receiver.State.Instance;
            h.State.Route = route; h.State.RouteIndex = r.Hosts.IndexOf(h);
        }
        string first = upstream.State.Parcel, second = downstream.State.Parcel;
        upstream.State.Progress = 1;
        r.Step();
        check(upstream.Status == "occupied" && upstream.NextAdvance > r.World.ElapsedMilliseconds && r.Total == 16,
            "An occupied next pipe holds the upstream batch with bounded retry backoff");
        downstream.State.Progress = 1;
        r.Step();
        check(upstream.State.Cargo == null && downstream.State.Parcel == first && r.Receiver.State.Parcel == second && r.Total == 16,
            "Vacating a pipe wakes its waiting upstream batch immediately while preserving one hop per parcel and all items");
    }

    private static void AutomaticOutlets(Action<bool, string> check)
    {
        foreach (string kind in new[] { "tube", "sender", "receiver" })
        {
            using var r = new Rig(); r.Fill();
            Set(r.Receiver, "Block", new Block { Code = new("gearwright:pneumatic-" + kind) });
            var outlet = r.Chest(r.Receiver.Pos.AddCopy(BlockFacing.EAST));
            bool conserved = true;
            for (int i = 0; i < 1200; i++)
            {
                r.Step();
                conserved &= r.Total + outlet.Inventory.Sum(s => s.StackSize) == 64;
                if (i % 17 == 0) r.Reload();
            }
            check(conserved && outlet.Inventory.Sum(s => s.StackSize) > 0 &&
                r.Target.Inventory.Sum(s => s.StackSize) + outlet.Inventory.Sum(s => s.StackSize) == 64 &&
                (kind != "receiver" || r.Target.Inventory.Sum(s => s.StackSize) > 0),
                "A " + kind + " main outlet receives automatically while retaining its branch role and all cargo through reload");
        }
        using (var r = new Rig())
        {
            r.Fill(8);
            foreach (var h in r.Hosts.Where(h => h != r.Sender)) { r.System.Unregister(h); r.Entities.Remove(h.Pos); }
            var outlet = r.Chest(r.Sender.Pos.AddCopy(BlockFacing.EAST));
            bool sawReturn = false;
            for (int i = 0; i < 500; i++) { r.Step(); sawReturn |= r.Sender.State.Returning; }
            check(r.Source.Inventory.All(s => s.Empty) && outlet.Inventory.Sum(s => s.StackSize) == 8 && sawReturn,
                "A sender loads directly from its branch into its own outlet chest, then returns the empty tray");
        }
        using (var r = new Rig())
        {
            r.Fill(8); Set(r.Receiver, "Block", new Block { Code = new("gearwright:pneumatic-tube") });
            var outlet = r.Chest(r.Receiver.Pos.AddCopy(BlockFacing.EAST));
            r.Locked.Add(outlet.Pos);
            for (int i = 0; i < 150; i++) r.Step();
            check(r.Source.Inventory[0].StackSize == 8, "Automatic outlets honor native inventory locks before ordering");
            r.Locked.Clear();
            for (int i = 0; i < 200 && r.Hosts.All(h => h.State.Cargo == null); i++) r.Step();
            var originalOutlet = outlet;
            check(r.Store.LastPositions.Contains(originalOutlet.Pos),
                "An automatic outlet reservation durably includes its destination chest identity");
            outlet = r.Chest(outlet.Pos.Copy());
            for (int i = 0; i < 200; i++) { r.Step(); if (i % 13 == 0) r.Reload(); }
            check(outlet.Inventory.All(s => s.Empty) && r.Total == 8 && r.Receiver.State.Cargo != null &&
                r.Receiver.State.DeliveryFace == BlockFacing.EAST && r.Receiver.State.DeliveryInventory == originalOutlet.GetBehavior<BEBehaviorPneumaticChest>().Instance,
                "A replaced outlet inventory cannot take a batch reserved for the original inventory");
            r.Entities[outlet.Pos] = originalOutlet;
            r.Receiver.State.Output = BlockFacing.NORTH;
            for (int i = 0; i < 30; i++) r.Step();
            check(originalOutlet.Inventory.All(s => s.Empty) && r.Receiver.State.Cargo != null,
                "Changing an automatic receiver's output cannot redirect its frozen delivery face");
            r.Receiver.State.Output = BlockFacing.EAST;
            for (int i = 0; i < 100; i++) r.Step();
            check(originalOutlet.Inventory.Sum(s => s.StackSize) == 8 && r.Hosts.All(h => h.State.Cargo == null),
                "Restoring the original outlet and inventory completes the held batch exactly once");
        }
    }
}
