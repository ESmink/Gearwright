using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gearwright.Air;
using Gearwright.Mechanics;
using Gearwright.Pneumatics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticAirFixture
{
    public static void Run(Action<bool, string> check)
    {
        IntakeUsesDeliveredAmounts(check);
        IntakeProtectsSavedState(check);
        AdapterUsesServerAirDelivery(check);
        DirectLinesConsumeFiniteAir(check);
        DirectLinesRejectInvalidWork(check);
        BalanceAndRange(check);
    }

    private static void IntakeUsesDeliveredAmounts(Action<bool, string> check)
    {
        foreach (BlockFacing outlet in BlockFacing.ALLFACES)
        {
            var state = PneumaticIntakeState.Read(null);
            state.SetOutlet(outlet);
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                double received = state.Receive(new(.1, .1, face.Opposite));
                check(Near(received, face == outlet ? 0 : 1),
                    "Pneumatic intake " + outlet.Code + " validates receiving face " + face.Code);
            }
            check(Near(state.StoredAir, 5), "Each of the five free faces contributes actual air once");
        }

        var intake = PneumaticIntakeState.Read(null);
        intake.Receive(new(.2, .1, BlockFacing.NORTH));
        intake.Receive(new(.1, .4, BlockFacing.WEST));
        check(Near(intake.StoredAir, 6), "Unequal bellows intervals add delivered amounts, not rates");
        foreach (var flow in new[] { new AirFlow(double.NaN, .1, BlockFacing.NORTH),
                     new AirFlow(double.PositiveInfinity, .1, BlockFacing.NORTH),
                     new AirFlow(-1, .1, BlockFacing.NORTH), new AirFlow(1, 0, BlockFacing.NORTH),
                     new AirFlow(1, double.NaN, BlockFacing.NORTH), new AirFlow(1, .1, null!) })
            check(intake.Receive(flow) == 0 && Near(intake.StoredAir, 6), "Malformed bellows flow leaves intake air untouched");

        check(Near(intake.Receive(new(100, 1, BlockFacing.NORTH)), PneumaticAir.PlenumCapacity - 6) && intake.StoredAir == PneumaticAir.PlenumCapacity,
            "Pneumatic plenum caps stored air and vents the excess");
        foreach (double seconds in new[] { 0, -1, double.NaN, double.PositiveInfinity, 1 })
            check(intake.Release(seconds) == 0 && intake.StoredAir == PneumaticAir.PlenumCapacity, "Invalid or oversized simulation intervals do not drain stored air");
        double total = 0;
        bool bounded = true;
        for (int i = 0; i < 100; i++)
        {
            double released = intake.Release(.1);
            total += released;
            bounded &= released >= 0 && released <= PneumaticAir.OutletUnitsPerSecond * .1;
        }
        check(bounded && Near(total + intake.StoredAir, PneumaticAir.PlenumCapacity) && intake.StoredAir < PneumaticAir.PlenumCapacity,
            "Stopped bellows debit only finite stored air and slow their drain as the reserve falls");
    }

    private static TreeAttribute OldestState()
    {
        var fixture = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-intake-schema1.json")));
        var tree = new TreeAttribute();
        tree.SetInt("schemaVersion", fixture["schemaVersion"].AsInt());
        tree.SetString("outlet", fixture["outlet"].AsString());
        tree.SetDouble("storedAir", fixture["storedAir"].AsDouble());
        tree.SetString("future-note", fixture["future-note"].AsString());
        return tree;
    }

    private static void IntakeProtectsSavedState(Action<bool, string> check)
    {
        var original = OldestState();
        var nested = new TreeAttribute(); nested.SetString("note", "nested unknown data");
        original["future-config"] = nested;
        byte[] before = Bytes(original);
        var state = PneumaticIntakeState.Read(original);
        check(state.CanWrite && state.Outlet == BlockFacing.EAST && Near(state.StoredAir, 12.5),
            "Oldest pneumatic intake fixture loads its outlet and finite air");
        state.Release(.1);
        double expected = state.StoredAir;
        var saved = (ITreeAttribute)state.Write();
        var reloaded = PneumaticIntakeState.Read(saved);
        check(reloaded.CanWrite && Near(reloaded.StoredAir, expected) && reloaded.Outlet == BlockFacing.EAST && saved.GetInt("schemaVersion") == 2 &&
              saved.GetString("future-note") == "preserve this field" &&
              saved.GetTreeAttribute("future-config").GetString("note") == "nested unknown data" &&
              before.SequenceEqual(Bytes(original)),
            "Intake load/save/reload preserves unknown fields and never edits the input tree");
        saved.SetDouble("storedAir", 0);
        check(Near(reloaded.StoredAir, expected), "Saved snapshots cannot mutate the live intake");

        var defaults = new TreeAttribute(); defaults.SetInt("schemaVersion", 1);
        var defaultState = PneumaticIntakeState.Read(defaults);
        check(defaultState.CanWrite && defaultState.StoredAir == 0 && defaultState.Outlet == BlockFacing.NORTH &&
              ((ITreeAttribute)PneumaticIntakeState.Read(null).Write()).GetInt("schemaVersion") == 2,
            "Old intake defaults migrate to schema two without inventing air");

        var invalid = new List<IAttribute> { new StringAttribute("unrecognised outer state") };
        foreach (int version in new[] { -1, 0, 3, int.MaxValue })
        {
            var bad = OldestState(); bad.SetInt("schemaVersion", version); invalid.Add(bad);
        }
        var missing = OldestState(); missing.RemoveAttribute("schemaVersion"); invalid.Add(missing);
        var typedVersion = OldestState(); typedVersion.SetDouble("schemaVersion", 1); invalid.Add(typedVersion);
        var typedAir = OldestState(); typedAir.SetString("storedAir", "do not overwrite"); invalid.Add(typedAir);
        foreach (double air in new[] { -1, 41, double.NaN, double.PositiveInfinity })
        {
            var bad = OldestState(); bad.SetDouble("storedAir", air); invalid.Add(bad);
        }
        var wrongFace = OldestState(); wrongFace.SetString("outlet", "somewhere"); invalid.Add(wrongFace);
        var typedFace = OldestState(); typedFace.SetInt("outlet", 1); invalid.Add(typedFace);
        foreach (var bad in invalid)
        {
            before = Bytes(bad);
            var protectedState = PneumaticIntakeState.Read(bad);
            check(!protectedState.CanWrite && protectedState.Problem != null &&
                  protectedState.Receive(new(1, .1, BlockFacing.NORTH)) == 0 && protectedState.Release(.1) == 0 &&
                  !protectedState.SetOutlet(BlockFacing.SOUTH) && before.SequenceEqual(Bytes(protectedState.Write())),
                "Malformed, unsupported or newer pneumatic data remains byte-identical and cannot operate");
        }
        var future = OldestState(); future.SetInt("schemaVersion", 3);
        check(PneumaticIntakeState.Read(future).Problem == "newer", "Future pneumatic schemas have a distinct diagnostic");
        var current = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "pneumatic-intake-schema2.json")));
        var expanded = OldestState(); expanded.SetInt("schemaVersion", current["schemaVersion"].AsInt());
        expanded.SetDouble("storedAir", current["storedAir"].AsDouble());
        var expandedState = PneumaticIntakeState.Read(expanded);
        check(expandedState.CanWrite && Near(PneumaticIntakeState.Read(expandedState.Write()).StoredAir, 1200),
            "Schema two retains a reserve above the old capacity through save/reload");
        expanded.SetDouble("storedAir", 2401);
        check(!PneumaticIntakeState.Read(expanded).CanWrite && Bytes(expanded).SequenceEqual(Bytes(PneumaticIntakeState.Read(expanded).Write())),
            "Schema two still protects over-capacity air byte-identically");
    }

    private static void AdapterUsesServerAirDelivery(Action<bool, string> check)
    {
        var entities = new Dictionary<BlockPos, BlockEntity>();
        var unloaded = new HashSet<BlockPos>();
        var errors = new List<string>();
        var chunk = Stub.Create<IWorldChunk>((_, _) => null);
        var accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetChunkAtBlockPos" => unloaded.Contains((BlockPos)args![0]!) ? null : chunk,
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetBlock" => entities.GetValueOrDefault((BlockPos)args![0]!)?.Block ?? new Block(),
            _ => null
        });
        var logger = Stub.Create<ILogger>((method, args) =>
        {
            if (method.Name == "Error") errors.Add(args![0]!.ToString()!);
            return null;
        });
        var world = Stub.Create<IWorldAccessor>((method, _) => method.Name switch
        { "get_BlockAccessor" => accessor, "get_Logger" => logger, _ => null });
        var server = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Server, _ => null });
        var client = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Client, _ => null });
        var intake = new BlockEntityPneumaticAirIntake { Api = server, Pos = new BlockPos(0, 0, 0) };
        Set(intake, "Block", new Block { Code = new AssetLocation("gearwright:test-pneumatic-intake") }); entities[intake.Pos] = intake;
        intake.SetOutlet(BlockFacing.EAST);
        check(AirDelivery.Deliver(server, intake.Pos.AddCopy(BlockFacing.WEST), new(.2, .1, BlockFacing.EAST)) &&
              AirDelivery.Deliver(server, intake.Pos.AddCopy(BlockFacing.UP), new(.1, .4, BlockFacing.DOWN)) && Near(intake.StoredAir, 6),
            "Native AirDelivery finds the pneumatic entity and combines independent bellows amounts");
        AirDelivery.Deliver(server, intake.Pos.AddCopy(BlockFacing.EAST), new(1, .1, BlockFacing.WEST));
        intake.ReceiveAir(world, intake.Pos.AddCopy(BlockFacing.NORTH), new(1, .1, BlockFacing.EAST));
        check(Near(intake.StoredAir, 6), "Adapter rejects its tube outlet and mismatched target positions");
        unloaded.Add(intake.Pos.AddCopy(BlockFacing.WEST));
        check(!AirDelivery.Deliver(server, intake.Pos.AddCopy(BlockFacing.WEST), new(.2, .1, BlockFacing.EAST)),
            "An unloaded bellows cannot supply the pneumatic adapter");
        unloaded.Add(intake.Pos);
        intake.ReceiveAir(world, intake.Pos, new(1, .1, BlockFacing.EAST));
        check(intake.ReleaseAir(.1) == 0 && Near(intake.StoredAir, 6), "Unloaded pneumatic intakes neither collect nor release air");
        unloaded.Clear(); intake.Api = client;
        intake.ReceiveAir(world, intake.Pos, new(1, .1, BlockFacing.EAST));
        check(!intake.SetOutlet(BlockFacing.DOWN) && intake.ReleaseAir(.1) == 0 && Near(intake.StoredAir, 6),
            "Client intake calls cannot collect, release or reconfigure server air");
        intake.Api = server;
        double released = intake.ReleaseAir(.1);
        double remaining = intake.StoredAir;
        check(released > 0 && Near(released + remaining, 6), "Server intake releases and debits its finite budget");
        var saved = new TreeAttribute(); intake.ToTreeAttributes(saved);
        var restored = new BlockEntityPneumaticAirIntake { Api = server, Pos = intake.Pos.Copy() };
        Set(restored, "Block", intake.Block); restored.FromTreeAttributes(saved, world);
        check(Near(restored.StoredAir, remaining) && restored.Outlet == BlockFacing.EAST,
            "SDK block-entity save/reload retains the remaining plenum and configured outlet");
        foreach (IAttribute invalid in new IAttribute[] { new StringAttribute("opaque original"), FutureTree() })
        {
            saved[BlockEntityPneumaticAirIntake.StorageKey] = invalid;
            byte[] before = Bytes(invalid);
            restored.FromTreeAttributes(saved, world);
            restored.ReceiveAir(world, restored.Pos, new(1, .1, BlockFacing.NORTH));
            check(!restored.CanWriteState && restored.ReleaseAir(.1) == 0 && !restored.SetOutlet(BlockFacing.UP),
                "Protected intake entity disables air and configuration changes");
            var rewritten = new TreeAttribute(); restored.ToTreeAttributes(rewritten);
            check(before.SequenceEqual(Bytes(rewritten[BlockEntityPneumaticAirIntake.StorageKey])),
                "SDK entity writes protected pneumatic attributes back byte-identically");
        }
        check(errors.Count == 2 && errors.All(message => message.Contains("read-only")),
            "Protected intake loads explain the pause through the game logger");
    }

    private static IAttribute FutureTree()
    {
        var tree = OldestState(); tree.SetInt("schemaVersion", 3); return tree;
    }

    private static PneumaticPosition P(int x, int y = 0, int z = 0, int dimension = 0) => new(dimension, x, y, z);

    private static PneumaticLineNode[] Line() => new[]
    {
        new PneumaticLineNode(P(0), PneumaticLineKind.Intake, null, BlockFacing.EAST),
        new PneumaticLineNode(P(1), PneumaticLineKind.Sender, BlockFacing.WEST, BlockFacing.EAST),
        new PneumaticLineNode(P(2), PneumaticLineKind.Tube, BlockFacing.WEST, BlockFacing.UP),
        new PneumaticLineNode(P(2, 1), PneumaticLineKind.Tube, BlockFacing.DOWN, BlockFacing.EAST),
        new PneumaticLineNode(P(3, 1), PneumaticLineKind.InlineReceiver, BlockFacing.WEST, BlockFacing.EAST),
        new PneumaticLineNode(P(4, 1), PneumaticLineKind.EndReceiver, BlockFacing.WEST, null)
    };

    private static void DirectLinesConsumeFiniteAir(Action<bool, string> check)
    {
        var nodes = Line();
        var supply = new[] { new PneumaticSupply(P(0), 1.5) };
        var result = PneumaticLineFlow.Solve(nodes, supply, .1);
        check(result.Status == "ok" && result.Sections.Count == 6 && Near(result.Consumed, .17) && Near(result.Vented, 1.33),
            "Directed sender, elbows and inline/end receivers deduct one cost per component and conserve the root budget");
        check(result.Sections.Values.All(flow => flow.Root == P(0) && Near(flow.Received, flow.Consumed + flow.Forwarded)) &&
              Near(result.Sections[P(4, 1)].Received, 1.38),
            "Downstream sections retain source provenance without restoring initial strength");
        var shuffled = PneumaticLineFlow.Solve(nodes.Reverse().ToArray(), supply, .1);
        check(result.Sections.All(pair => shuffled.Sections[pair.Key] == pair.Value),
            "Direct-line flow is independent of snapshot enumeration order");
        var stopped = PneumaticLineFlow.Solve(nodes, Array.Empty<PneumaticSupply>(), .1);
        check(stopped.Sections.Count == 0 && stopped.Consumed == 0 && stopped.Vented == 0,
            "A later interval with no released air cannot reuse downstream offers");
        var starved = PneumaticLineFlow.Solve(nodes, new[] { new PneumaticSupply(P(0), .04) }, .1);
        check(starved.Sections.Count == 2 && Near(starved.Consumed, .04) && starved.Vented == 0 &&
              starved.Sections[P(1)].Forwarded == 0, "Insufficient air is consumed locally and cannot power later sections");
        foreach (var barrier in new[] { nodes[2] with { Loaded = false }, nodes[2] with { Writable = false },
                     nodes[2] with { Input = BlockFacing.SOUTH } })
        {
            var blocked = nodes.ToArray(); blocked[2] = barrier;
            var flow = PneumaticLineFlow.Solve(blocked, supply, .1);
            check(flow.Sections.Count == 2 && Near(flow.Consumed + flow.Vented, 1.5),
                "Unloaded, protected or reversed connections stop air without creating a new source");
        }
        var broken = PneumaticLineFlow.Solve(nodes.Where(n => n.Position != P(2)).ToArray(), supply, .1);
        check(broken.Sections.Count == 2 && Near(broken.Vented, 1.45), "Removing a later section vents at the surviving open end");
        var otherDimension = nodes.Select(n => n with { Position = n.Position with { Dimension = 1 } }).ToArray();
        var separate = PneumaticLineFlow.Solve(nodes.Concat(otherDimension).ToArray(),
            new[] { supply[0], new PneumaticSupply(P(0, dimension: 1), 1) }, .1);
        check(separate.Sections.Count == 12 && Near(separate.Consumed + separate.Vented, 2.5) &&
              separate.Sections[P(4, 1, dimension: 1)].Root.Dimension == 1,
            "Equal coordinates in different dimensions remain separate air networks");

        // A correctly directed closed loop has no intake, so it can never bootstrap air.
        var loop = new[]
        {
            new PneumaticLineNode(P(0), PneumaticLineKind.Tube, BlockFacing.SOUTH, BlockFacing.EAST),
            new PneumaticLineNode(P(1), PneumaticLineKind.Tube, BlockFacing.WEST, BlockFacing.SOUTH),
            new PneumaticLineNode(P(1, 0, 1), PneumaticLineKind.Tube, BlockFacing.NORTH, BlockFacing.WEST),
            new PneumaticLineNode(P(0, 0, 1), PneumaticLineKind.Tube, BlockFacing.EAST, BlockFacing.NORTH)
        };
        check(PneumaticLineFlow.Solve(loop, Array.Empty<PneumaticSupply>(), .1).Sections.Count == 0 &&
              PneumaticLineFlow.Solve(loop, supply, .1).Status == "invalid-supply",
            "Directed loops stay unpowered and ordinary tubes cannot masquerade as roots");
    }

    private static void DirectLinesRejectInvalidWork(Action<bool, string> check)
    {
        var nodes = Line();
        var supply = new PneumaticSupply(P(0), 1.5);
        foreach (var bad in new[] { supply with { Amount = double.NaN }, supply with { Amount = -1 },
                     supply with { Amount = 25 }, supply with { Intake = P(90) } })
            check(PneumaticLineFlow.Solve(nodes, new[] { bad }, .1).Status == "invalid-supply", "Invalid root budgets are rejected before propagation");
        check(PneumaticLineFlow.Solve(nodes, new[] { supply, supply }, .1).Status == "invalid-supply",
            "One intake cannot contribute its budget twice in a common interval");
        check(PneumaticLineFlow.Solve(nodes.Append(nodes[0]).ToArray(), new[] { supply }, .1).Status == "invalid-topology",
            "Duplicate physical components cannot be charged or powered twice");
        var bentSender = nodes.ToArray(); bentSender[1] = nodes[1] with { Output = BlockFacing.UP };
        check(PneumaticLineFlow.Solve(bentSender, new[] { supply }, .1).Status == "invalid-topology",
            "Inventory branches must retain a straight mainline");
        var invalidKind = nodes.ToArray(); invalidKind[2] = nodes[2] with { Kind = (PneumaticLineKind)999 };
        check(PneumaticLineFlow.Solve(invalidKind, new[] { supply }, .1).Status == "invalid-topology",
            "Unsupported components are rejected instead of treated as ordinary tubes");
        var huge = Enumerable.Range(0, PneumaticAir.MaximumNodes + 1).Select(x => nodes[0] with { Position = P(x) }).ToArray();
        check(PneumaticLineFlow.Solve(huge, new[] { supply }, .1).Status == "network-limit" &&
              PneumaticLineFlow.Solve(nodes, new[] { supply }, 1).Status == "invalid-interval",
            "Snapshot size and simulation time are bounded before network work");
    }

    private static bool Near(double left, double right) => Math.Abs(left - right) < 1e-9;

    private static void BalanceAndRange(Action<bool, string> check)
    {
        double lowerStroke = LargeBellowsMotion.PumpedAir(0, Math.PI * 2, false);
        check(Math.Abs(lowerStroke * PneumaticAir.UnitsPerBellowsUnit - 20) < .02,
            "Actual lower bellow linkage supplies twenty transport units per revolution");
        foreach (bool upper in new[] { false, true })
        {
            var intake = PneumaticIntakeState.Read(null);
            double bellowsReserve = 0, received = 0, released = 0, min = 1, max = 0;
            // Sample real linkage volume, retaining the lower chamber and its nozzle ceiling.
            for (int tick = 0; tick < 3000; tick++)
            {
                double from = (tick % 10) * Math.PI / 5, to = from + Math.PI / 5;
                double pumped = LargeBellowsMotion.PumpedAir(from, to, upper);
                bellowsReserve = Math.Min(LargeBellowsStateSchema.Capacity, bellowsReserve + pumped);
                double delivered = upper ? pumped : Math.Min(bellowsReserve, BEBehaviorLargeBellows.MaximumOutletAirPerSecond * .1);
                bellowsReserve -= delivered;
                received += intake.Receive(new(delivered * 3 / .1, .1, BlockFacing.NORTH));
                released += intake.Release(.1);
                if (tick >= 2900) { double fill = intake.StoredAir / PneumaticAir.PlenumCapacity; min = Math.Min(min, fill); max = Math.Max(max, fill); }
            }
            check(min > .47 && max < (upper ? .58 : .53) && Math.Abs(received - released - intake.StoredAir) < 1e-7,
                "Three " + (upper ? "upper" : "lower") + " bellows at one revolution/second settle near half reserve without creating air (" + min.ToString("P1") + "–" + max.ToString("P1") + ")");
        }
        check(Near(PneumaticAir.OutletRate(600), 15) && Near(PneumaticAir.OutletRate(1200), 60) && Near(PneumaticAir.OutletRate(2400), 240),
            "Quarter, half and full reservoirs drain at fifteen, sixty and 240 units/second");
        foreach (var (tubes, rate) in new[] { (128, 20.0), (512, 60.0) })
        {
            var nodes = new List<PneumaticLineNode> { new(P(0), PneumaticLineKind.Intake, null, BlockFacing.EAST),
                new(P(1), PneumaticLineKind.Sender, BlockFacing.WEST, BlockFacing.EAST) };
            nodes.AddRange(Enumerable.Range(2, tubes).Select(x => new PneumaticLineNode(P(x), PneumaticLineKind.Tube, BlockFacing.WEST, BlockFacing.EAST)));
            var end = P(tubes + 2); nodes.Add(new(end, PneumaticLineKind.EndReceiver, BlockFacing.WEST, null));
            var movement = new Dictionary<PneumaticPosition, double> { [P(tubes)] = .4, [end] = .125 };
            var result = PneumaticLineFlow.Solve(nodes, new[] { new PneumaticSupply(P(0), rate * .1) }, .1, movement);
            check(result.Status == "ok" && result.Sections.Count == nodes.Count && result.Sections[end].Movement == .125 &&
                result.Sections[P(tubes)].Movement == .4 && Math.Abs(result.Consumed + result.Vented - rate * .1) < 1e-8,
                tubes + " tubes retain air for cargo and receiver work at " + rate + " units/second");
        }
    }

    private static byte[] Bytes(IAttribute attribute)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        attribute.ToBytes(writer);
        writer.Flush();
        return stream.ToArray();
    }
}
