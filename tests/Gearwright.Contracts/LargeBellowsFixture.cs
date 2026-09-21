using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Gearwright.Air;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class LargeBellowsFixture
{
    private sealed class Receiver : Block, IBellowsAirReceiver
    {
        public double Received;
        public void BlowAirInto(IWorldAccessor world, BlockPos pos, float amount, BlockFacing direction) => Received += amount;
    }
    private sealed class AirConsumer : BlockEntity, IAirReceiver, IBellowsAirReceiver
    {
        public readonly List<AirFlow> Flows = new();
        public int LegacyCalls;
        public void ReceiveAir(IWorldAccessor world, BlockPos position, AirFlow flow) => Flows.Add(flow);
        public void BlowAirInto(IWorldAccessor world, BlockPos position, float amount, BlockFacing direction) => LegacyCalls++;
    }
    private sealed class ConsumerBehavior : BlockEntityBehavior, IAirReceiver
    {
        public double Received;
        public ConsumerBehavior(BlockEntity owner) : base(owner) { }
        public void ReceiveAir(IWorldAccessor world, BlockPos position, AirFlow flow) => Received += flow.Amount;
    }
    public static void Run(Action<bool, string> check)
    {
        Dictionary<BlockPos, BlockEntity> entities = new();
        HashSet<BlockPos> unloaded = new();
        var receiver = new Receiver();
        IWorldChunk chunk = Stub.Create<IWorldChunk>((_, _) => null);
        var accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetChunkAtBlockPos" => unloaded.Contains((BlockPos)args![0]!) ? null : chunk,
            "GetBlock" => entities.GetValueOrDefault((BlockPos)args![0]!)?.Block ?? receiver,
            _ => null
        });
        var world = Stub.Create<IWorldAccessor>((method, _) => method.Name switch
        {
            "get_BlockAccessor" => accessor, "get_Logger" => Stub.Create<ILogger>((_, _) => null), _ => null
        });
        var api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Server, _ => null });
        foreach (BlockFacing nozzle in BlockFacing.HORIZONTALS)
        {
            entities.Clear(); unloaded.Clear();
            var owner = new BlockEntityMechPoweredBellows { Api = api, Pos = new BlockPos(0, 0, 0) };
            var block = new BlockAutomaticBellow { Code = new("gearwright:automatic-bellow-" + nozzle.Code) };
            block.Variant = new(new Dictionary<string, string> { ["side"] = nozzle.Code });
            Set(owner, "Block", block);
            var bellows = new BEBehaviorLargeBellows(owner); Set(bellows, "Api", api); owner.Behaviors.Add(bellows);
            entities[owner.Pos] = owner;
            BEBehaviorMPLateralCrank Crank(BlockFacing vertical, bool aligned = true)
            {
                var entity = new BlockEntityMechPoweredBellows { Api = api, Pos = bellows.RearPosition.AddCopy(vertical) };
                bool x = bellows.ShaftFace.Axis == EnumAxis.X;
                var crankBlock = new BlockLateralCrank { Code = new("gearwright:lateral-crank-" + (x == aligned ? "we" : "ns")) };
                crankBlock.Variant = new(new Dictionary<string, string> { ["rotation"] = x == aligned ? "we" : "ns" });
                Set(entity, "Block", crankBlock);
                var drive = new BEBehaviorMPLateralCrank(entity); entity.Behaviors.Add(drive);
                Set(drive, "Api", api); Set(drive, "gearedRatio", 1f); Set(drive, "network", new MechanicalNetwork());
                entities[entity.Pos] = entity;
                return drive;
            }
            var upper = Crank(BlockFacing.UP);
            check(ReferenceEquals(bellows.SelectedDrive(), upper) && upper.AttachedDevices.Single() == bellows,
                "Large bellows finds the upper crank behind its " + nozzle.Code + " nozzle");
            var lower = Crank(BlockFacing.DOWN);
            check(ReferenceEquals(bellows.SelectedDrive(), lower) && lower.AttachedDevices.Single() == bellows && upper.AttachedDevices.Length == 0,
                "Both shafts attach exactly once, with a stopped lower shaft preferred");
            foreach (var drive in new[] { lower, upper })
            {
                if (drive == upper) entities.Remove(lower.Position);
                bool alignedOrbit = true;
                for (int i = 0; i < 360; i += 5)
                {
                    double angle = i * Math.PI / 180;
                    drive.Network!.AngleRad = (float)angle;
                    BellowsPose pose = LargeBellowsMotion.Pose(LargeBellowsMotion.Phase(drive.DeviceAngle(bellows), drive == upper), drive == upper);
                    float[] worldPoint = Mat4f.MulWithVec4(PumpOrientation.Matrix(nozzle.Opposite, BlockFacing.UP),
                        new[] { (float)pose.Journal.X / 16, (float)pose.Journal.Y / 16, .5f, 1f });
                    Vec3i axis = drive.AxisFaces()[0].Normali;
                    double expectedX = drive.Position.X + .5 - axis.Z * Math.Sin(angle) * 3 / 16;
                    double expectedY = drive.Position.Y + .5 + Math.Cos(angle) * 3 / 16;
                    double expectedZ = drive.Position.Z + .5 + axis.X * Math.Sin(angle) * 3 / 16;
                    alignedOrbit &= Math.Abs(worldPoint[0] - expectedX) < 1e-6 && Math.Abs(worldPoint[1] - expectedY) < 1e-6 && Math.Abs(worldPoint[2] - expectedZ) < 1e-6;
                }
                check(alignedOrbit, "Bellows journal follows the rendered world orbit for " + nozzle.Code + (drive == upper ? " upper" : " lower"));
            }
            entities[lower.Position] = lower.Blockentity;
            unloaded.Add(lower.Position);
            check(bellows.SelectedDrive() == null && upper.AttachedDevices.Length == 0,
                "An unloaded lower position cannot cause the upper network to take ownership");
            unloaded.Clear(); entities.Remove(lower.Position);
            check(ReferenceEquals(bellows.SelectedDrive(), upper), "Removing the lower crank enables the upper crank");
            lower = Crank(BlockFacing.DOWN, aligned: false);
            check(ReferenceEquals(bellows.SelectedDrive(), upper), "A misaligned lower shaft does not hide the valid upper shaft");
            entities.Remove(lower.Position);
            double before = receiver.Received;
            for (int i = 1; i <= 720; i++)
            {
                upper.Network!.AngleRad = i * GameMath.TWOPI / 720;
                upper.Network.Speed = .2f;
                bellows.StepReciprocatingDrive(Math.PI * 2 / 720);
            }
            check(receiver.Received > before && receiver.Received - before < .4,
                "Top-driven bellows sends bounded air to the actual vanilla receiver interface");
            upper.Network!.Speed = 0; before = receiver.Received;
            bellows.StepReciprocatingDrive(.1);
            check(receiver.Received == before, "A stopped bellows delivers no new pump stroke");

            lower = Crank(BlockFacing.DOWN);
            before = receiver.Received;
            for (int i = 1; i <= 720; i++)
            {
                lower.Network!.AngleRad = (float)(-i * Math.PI * 2 / 720);
                lower.Network.Speed = -.2f;
                bellows.StepReciprocatingDrive(Math.PI * 2 / 720);
            }
            check(bellows.ReservoirFill > .49 && bellows.ReservoirFill < .51 && receiver.Received == before,
                "Reverse lower strokes store one swept volume in the upper chamber before discharge");
            typeof(BEBehaviorLargeBellows).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bellows, new object[] { .1f });
            check(receiver.Received > before && receiver.Received - before <= .015001 && bellows.ReservoirFill > 0,
                "Stored air supplies the nozzle while the lower stroke pauses");

            var client = Stub.Create<ICoreAPI>((method, _) => method.Name switch
                { "get_World" => world, "get_Side" => EnumAppSide.Client, _ => null });
            double fillBefore = bellows.ReservoirFill; before = receiver.Received;
            Set(bellows, "Api", client); bellows.StepReciprocatingDrive(.1); Set(bellows, "Api", api);
            check(bellows.ReservoirFill == fillBefore && receiver.Received == before, "Clients cannot create or deliver bellows air");

            BlockPos target = owner.Pos.AddCopy(nozzle);
            var consumer = new AirConsumer { Api = api, Pos = target };
            Set(consumer, "Block", new Block()); entities[target] = consumer;
            entities.Remove(lower.Position);
            double[] amounts = new double[2], rates = new double[2];
            for (int speed = 1; speed <= 2; speed++)
            {
                consumer.Flows.Clear();
                for (int i = 1; i <= 720; i++)
                {
                    upper.Network!.AngleRad = i * GameMath.TWOPI / 720;
                    upper.Network.Speed = .2f * speed;
                    bellows.StepReciprocatingDrive(Math.PI * 2 / 720 / speed);
                }
                amounts[speed - 1] = consumer.Flows.Sum(flow => flow.Amount);
                rates[speed - 1] = consumer.Flows.Max(flow => flow.Rate);
            }
            check(Math.Abs(amounts[0] - amounts[1]) < 1e-6 && Math.Abs(rates[1] / rates[0] - 2) < 1e-6 &&
                  consumer.Flows.All(flow => flow.IsValid && flow.Direction == nozzle) && consumer.LegacyCalls == 0,
                "Software air consumers receive tick duration and actual flow strength; double speed doubles rate without doubling one stroke");
            int deliveries = consumer.Flows.Count;
            foreach (AirFlow invalid in new[] { new AirFlow(0, .1, nozzle), new AirFlow(-1, .1, nozzle),
                new AirFlow(double.NaN, .1, nozzle), new AirFlow(1, 0, nozzle), new AirFlow(1, double.PositiveInfinity, nozzle) })
                check(!AirDelivery.Deliver(api, owner.Pos, invalid), "Invalid or empty air delivery is rejected");
            check(!AirDelivery.Deliver(client, owner.Pos, new(.5, .1, nozzle)), "The air layer refuses client delivery");
            unloaded.Add(target);
            check(!AirDelivery.Deliver(api, owner.Pos, new(.5, .1, nozzle)), "Air cannot enter an unloaded consumer");
            unloaded.Clear(); unloaded.Add(owner.Pos);
            check(!AirDelivery.Deliver(api, owner.Pos, new(.5, .1, nozzle)), "An unloaded generator cannot deliver air");
            unloaded.Clear();
            check(consumer.Flows.Count == deliveries, "Rejected deliveries leave consumer state unchanged");
            var behaviorOwner = new BlockEntityMechPoweredBellows { Api = api, Pos = target };
            Set(behaviorOwner, "Block", new Block());
            var behavior = new ConsumerBehavior(behaviorOwner); behaviorOwner.Behaviors.Add(behavior);
            entities[target] = behaviorOwner;
            AirDelivery.Deliver(api, owner.Pos, new(.5, .1, nozzle));
            AirDelivery.Deliver(api, owner.Pos, new(.25, .2, nozzle));
            check(Math.Abs(behavior.Received - .1) < 1e-9, "Independent generators can supply an entity behavior through the same air contract");

            var forge = new BlockEntityForge { Api = api, Pos = target };
            Set(forge, "Block", new BlockForge()); entities[target] = forge;
            FieldInfo oxygen = typeof(BlockEntityForge).GetField("extraOxygenRate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            FieldInfo direction = typeof(BlockEntityForge).GetField("blowDirection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            foreach (var drive in new[] { upper, lower })
            {
                if (drive == lower) entities[lower.Position] = lower.Blockentity;
                Set(forge, "burning", true); Set(forge, "extraOxygenRate", 0f);
                for (int i = 1; i <= 720; i++)
                {
                    drive.Network!.AngleRad = i * GameMath.TWOPI / 720;
                    drive.Network.Speed = .2f;
                    bellows.StepReciprocatingDrive(Math.PI * 2 / 720);
                }
                if (drive == lower)
                    typeof(BEBehaviorLargeBellows).GetMethod("Tick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(bellows, new object[] { .1f });
                check((float)oxygen.GetValue(forge)! > 0 && ReferenceEquals(direction.GetValue(forge), nozzle),
                    "The actual burning vanilla forge receives " + (drive == upper ? "upper" : "lower") + " bellows airflow from " + nozzle.Code);
            }
            Set(forge, "burning", false); Set(forge, "extraOxygenRate", 0f);
            AirDelivery.Deliver(api, owner.Pos, new(.5, .1, nozzle));
            check((float)oxygen.GetValue(forge)! == 0, "Air does not light an unlit vanilla forge");
            entities.Remove(target);

            var saved = new TreeAttribute();
            var fixture = JsonObject.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "large-bellows-v1.json")));
            var state = new TreeAttribute(); state.SetInt("schemaVersion", fixture["schemaVersion"].AsInt());
            state.SetDouble("air", fixture["air"].AsDouble()); state.SetString("future-note", fixture["future-note"].AsString());
            saved[BEBehaviorLargeBellows.StorageKey] = state;
            bellows.FromTreeAttributes(saved, world);
            var roundTrip = new TreeAttribute(); bellows.ToTreeAttributes(roundTrip);
            var loaded = roundTrip.GetTreeAttribute(BEBehaviorLargeBellows.StorageKey);
            check(loaded.GetDouble("air") == .125 && loaded.GetString("future-note") == "preserve this field",
                "Bellows schema-1 fixture preserves stored air and unknown fields on round trip");
            state.SetInt("schemaVersion", 2); state.SetDouble("air", 42);
            bellows.FromTreeAttributes(saved, world); bellows.ToTreeAttributes(roundTrip);
            loaded = roundTrip.GetTreeAttribute(BEBehaviorLargeBellows.StorageKey);
            check(bellows.SelectedDrive() == null && loaded.GetInt("schemaVersion") == 2 && loaded.GetDouble("air") == 42,
                "A newer bellows document remains unchanged and cannot drive the machine");
            state.SetInt("schemaVersion", 1); state.SetString("air", "invalid air bytes");
            bellows.FromTreeAttributes(saved, world); bellows.ToTreeAttributes(roundTrip);
            check(bellows.SelectedDrive() == null && roundTrip.GetTreeAttribute(BEBehaviorLargeBellows.StorageKey).GetString("air") == "invalid air bytes",
                "Malformed schema-1 air is retained without conversion or reset");
            saved[BEBehaviorLargeBellows.StorageKey] = new StringAttribute("original malformed bytes");
            bellows.FromTreeAttributes(saved, world); bellows.ToTreeAttributes(roundTrip);
            check(roundTrip.GetString(BEBehaviorLargeBellows.StorageKey) == "original malformed bytes", "Malformed bellows data is preserved");
            bellows.FromTreeAttributes(new TreeAttribute(), world); bellows.ToTreeAttributes(roundTrip);
            check(roundTrip.GetTreeAttribute(BEBehaviorLargeBellows.StorageKey).GetInt("schemaVersion") == 1,
                "An existing vanilla bellows with no Gearwright state acquires additive schema 1");
        }
        foreach (bool top in new[] { false, true })
        foreach (int sign in new[] { -1, 1 })
        {
            double total = 0; bool closed = true, breathing = true;
            double minBottom = double.MaxValue, maxBottom = double.MinValue;
            for (int i = 0; i < 360; i++)
            {
                double phase = i * Math.PI / 180 * sign;
                BellowsPose p = LargeBellowsMotion.Pose(phase, top);
                BellowsPose next = LargeBellowsMotion.Pose(phase + sign * Math.PI / 180, top);
                minBottom = Math.Min(minBottom, p.BottomAngle); maxBottom = Math.Max(maxBottom, p.BottomAngle);
                bool intake = LargeBellowsMotion.IsTakingInAir(phase, sign * Math.PI / 180, top);
                breathing &= intake == (next.BottomAngle < p.BottomAngle) && !LargeBellowsMotion.IsTakingInAir(phase, 0, top);
                if (top) breathing &= intake == (next.UpperFill < p.UpperFill);
                closed &= Math.Abs((p.Input - p.Journal).Length - 7) < 1e-7 &&
                    Math.Abs((p.Output - p.Plate).Length - (top ? LargeBellowsMotion.TopLinkLength : 6.6)) < 1e-6;
                total += LargeBellowsMotion.PumpedAir(phase, phase + sign * Math.PI / 180, top);
                if (!top)
                {
                    var hinge = new ShapeElement { From = new[] { 3.9095, 7.9055, 1.0 },
                        RotationOrigin = new[] { 3.9095, 8.9055, 1.0 }, RotationZ = -12 };
                    float[] actual = Mat4f.MulWithVec4(LargeBellowsRenderer.ElementTransform(hinge, p.BottomAngle + 12 * GameMath.DEG2RAD).Values,
                        new[] { 20.9f / 16, -1f / 16, 7f / 16, 1 });
                    closed &= Math.Abs(actual[0] - p.Plate.X / 16) < 1e-6 && Math.Abs(actual[1] - p.Plate.Y / 16) < 1e-6 && Math.Abs(actual[2] - .5) < 1e-6;
                }
            }
            check(closed, "Bellows " + (top ? "upper" : "lower") + " joints close over a full revolution in direction " + sign);
            check(total > .19 && total < .3, "One bellows revolution supplies a bounded stroke in either direction");
            check(breathing && maxBottom - minBottom > 6 * GameMath.DEG2RAD,
                "The lower chamber breathes and opens its inlet on expansion in either mounting and rotation direction");
        }
    }
}
