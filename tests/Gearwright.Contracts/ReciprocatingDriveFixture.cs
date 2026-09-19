using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class ReciprocatingDriveFixture
{
    public static void Run(Action<bool, string> check)
    {
        Dictionary<BlockPos, BlockEntity> entities = new();
        IWorldChunk chunk = Stub.Create<IWorldChunk>((_, _) => null);
        bool loaded = true;
        Item water = new() { Code = new AssetLocation("game:waterportion"),
            Attributes = JsonObject.FromJson("{\"waterTightContainerProps\":{}}") };
        IBlockAccessor accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetChunkAtBlockPos" => loaded ? chunk : null,
            "GetBlock" => entities.GetValueOrDefault((BlockPos)args![0]!)?.Block ?? new Block { BlockMaterial = EnumBlockMaterial.Stone },
            _ => null
        });
        IWorldAccessor world = Stub.Create<IWorldAccessor>((method, _) => method.Name switch
        {
            "get_BlockAccessor" => accessor, "GetItem" => water,
            "get_Logger" => Stub.Create<ILogger>((_, _) => null), _ => null
        });
        ICoreAPI api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Side" => EnumAppSide.Server, _ => null });
        BlockEntity driveEntity = new FixtureEntity { Api = api, Pos = new BlockPos(0, 0, 0) };
        BlockLateralCrank block = new() { Code = new AssetLocation("gearwright:lateral-crank-we") };
        Set(driveEntity, "Block", block);
        BEBehaviorMPLateralCrank drive = new(driveEntity);
        driveEntity.Behaviors.Add(drive);
        Set(drive, "Api", api); Set(drive, "gearedRatio", 1f);
        MechanicalNetwork network = new();
        Set(drive, "network", network);
        network.nodes.Add(driveEntity.Pos, drive);
        MechanicalPowerMod manager = new();
        Set(manager, "serverNwChannel", Stub.Create<IServerNetworkChannel>((_, _) => null));
        Set(network, "mechanicalPowerMod", manager);
        float torque = .1f;
        network.nodes.Add(new BlockPos(2, 0, 0), Stub.Create<IMechanicalPowerNode>((method, args) =>
        {
            if (method.Name == "get_GearedRatio") return 1f;
            if (method.Name == "GetTorque") { args![2] = 0f; return torque; }
            return null;
        }));

        BlockEntityReciprocatingPump Pump(BlockFacing face, BlockFacing output, double amount = 0)
        {
            BlockPos pos = driveEntity.Pos.AddCopy(face);
            BlockEntityReciprocatingPump pump = new() { Api = api, Pos = pos };
            Set(pump, "Block", new BlockReciprocatingPump { Code = new AssetLocation("gearwright:reciprocating-pump") });
            TreeAttribute tree = new();
            tree.SetInt("posx", pos.X); tree.SetInt("posy", pos.Y); tree.SetInt("posz", pos.Z);
            TreeAttribute state = new();
            state.SetInt("schemaVersion", 1); state.SetString("driveFace", face.Opposite.Code);
            state.SetString("outputFace", output.Code); state.SetString("contentCode", water.Code.ToString());
            state.SetDouble("amountLitres", amount); state.SetDouble("lastVolumeLitres", 4.05);
            state.SetString("future-note", "keep"); tree["gearwrightReciprocatingPump"] = state;
            pump.FromTreeAttributes(tree, world);
            entities[pos] = pump;
            return pump;
        }

        foreach (EnumAxis axis in new[] { EnumAxis.X, EnumAxis.Z })
        {
            block.Variant = new Vintagestory.API.Util.RelaxedReadOnlyDictionary<string, string>(
                new Dictionary<string, string> { ["rotation"] = axis == EnumAxis.X ? "we" : "ns" });
            BlockFacing positive = axis == EnumAxis.X ? BlockFacing.EAST : BlockFacing.SOUTH;
            BlockFacing[] faces = ReciprocatingDriveMount.Faces(axis);
            for (int mask = 1; mask < 16; mask++)
            {
                entities.Clear(); entities[driveEntity.Pos] = driveEntity;
                // Reverse insertion order and alternate pipe direction: neither
                // may change the canonical physical seats.
                for (int index = 3; index >= 0; index--)
                    if ((mask & (1 << index)) != 0) Pump(faces[index], index % 2 == 0 ? positive : positive.Opposite);
                IReciprocatingDriveDevice[] devices = drive.AttachedDevices;
                float[] physical = devices.Select(d => drive.DeviceOffset(d) * (d.ShaftFace == positive ? 1 : -1)).ToArray();
                check(Math.Abs(physical.Sum()) < 1e-6 &&
                    physical.Select((value, index) => Math.Abs(value - ReciprocatingDriveMount.CenteredOffset(index, devices.Length)) < 1e-6).All(v => v),
                    $"{axis} shaft centers attachment mask {mask} independently of placement order and reversed plumbing");
                loaded = false;
                check(drive.AttachedDevices.Length == 0, "Unloaded device chunks contribute neither seats nor load");
                loaded = true;
            }

            entities.Clear(); entities[driveEntity.Pos] = driveEntity;
            foreach (BlockFacing face in faces) Pump(face, positive);
            MethodInfo planned = typeof(BlockLateralCrank).GetMethod("FindPlannedPumpRotation", BindingFlags.NonPublic | BindingFlags.Static)!;
            object?[] arguments = { world, driveEntity.Pos, 0, false };
            string? rotation = (string?)planned.Invoke(null, arguments);
            check(rotation == (axis == EnumAxis.X ? "we" : "ns") && (int)arguments[2]! == 4 && !(bool)arguments[3]!,
                "A shaft can be planned after four compatible pumps without the former one-pump restriction");
            foreach (BlockFacing face in faces)
            {
                BlockPos pos = driveEntity.Pos.AddCopy(face);
                BlockEntity existing = entities[pos]; entities.Remove(pos);
                object?[] placement = { world, pos, null, null, null };
                bool aligned = (bool)typeof(BlockReciprocatingPump).GetMethod("TryFindCrank", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, placement)!;
                check(aligned && ReferenceEquals(placement[4], drive) && ReferenceEquals(placement[2], face.Opposite),
                    "A new pump aligns to a shaft already carrying three other pumps");
                entities[pos] = existing;
            }

            PumpTimingSystem timing = new(); timing.Start(api);
            try
            {
                foreach (BlockFacing blockedFace in faces)
                foreach (int direction in new[] { -1, 1 })
                {
                    entities.Clear(); entities[driveEntity.Pos] = driveEntity;
                    BlockEntityReciprocatingPump[] pumps = faces.Select(face => Pump(face, positive, face == blockedFace ? 4 : 0)).ToArray();
                    BlockEntityReciprocatingPump blocked = pumps.Single(p => p.DriveFace == blockedFace.Opposite);
                    network.AngleRad = 0;
                    double origin = drive.PumpAngle(blocked), ratio = drive.PumpTravel(blocked, 1);
                    network.AngleRad = (float)((direction * Math.Sign(ratio) * .8 - origin) / ratio);
                    float startAngle = network.AngleRad;
                    network.Speed = direction * .6f; torque = direction * .1f;
                    double expectedLoad = ReciprocatingDriveMount.BearingResistance + pumps.Sum(p =>
                        (double)p.SampleReciprocatingLoad(drive.PumpAngle(p), drive.PumpTravel(p, network.Speed * .5), .1));
                    check(Math.Abs(drive.GetResistance() - expectedLoad) < 1e-5 * Math.Max(1, expectedLoad),
                        "The drive sums every device load once and adds shaft bearing friction once");
                    for (int tick = 1; tick <= 500; tick++) network.ServerTick(.02f, tick);
                    check(Math.Abs(network.Speed) < .001 && Math.Abs(network.AngleRad - startAngle) < Math.PI - .8 &&
                        pumps.Sum(p => p.ContentAmountLitres) == 4,
                        $"Blocked {axis}/{blockedFace.Code} pump stops all four devices before BDC without fluid loss, direction {direction}");
                    foreach (BlockEntityReciprocatingPump pump in pumps)
                    {
                        PumpPresentation frame = pump.CapturePresentation(drive);
                        check(frame.IsValid && Math.Abs(frame.Angle - drive.PumpAngle(pump)) < 1e-9 && frame.RodOffsetX == drive.DeviceOffset(pump),
                            "All device snapshots contain the shared stopped phase and their authoritative centered seat");
                        TreeAttribute saved = new(); pump.ToTreeAttributes(saved);
                        check(saved.GetTreeAttribute("gearwrightReciprocatingPump").GetString("future-note") == "keep" &&
                            !saved.GetTreeAttribute("gearwrightReciprocatingPump").HasAttribute("rodOffsetX"),
                            "Seat regrouping leaves saved orientation, contents and unknown fields compatible");
                    }
                    BlockEntityFluidPipe pipe = new() { Api = api, Pos = blocked.Pos.AddCopy(positive) };
                    TreeAttribute pipeTree = new();
                    pipeTree.SetInt("posx", pipe.Pos.X); pipeTree.SetInt("posy", pipe.Pos.Y); pipeTree.SetInt("posz", pipe.Pos.Z);
                    TreeAttribute pipeState = new(); pipeState.SetInt("schemaVersion", 7);
                    pipeState.SetInt("port-" + positive.Opposite.Index, 1); pipeTree["gearwrightHydraulics"] = pipeState;
                    pipe.FromTreeAttributes(pipeTree, world); entities[pipe.Pos] = pipe;
                    blocked.SetTransferPort(positive, pipe, 0);
                    double stoppedAngle = network.AngleRad;
                    for (int tick = 501; tick <= 750; tick++) network.ServerTick(.02f, tick);
                    check(pipe.ContentAmountLitres > .01 && Math.Abs(network.AngleRad - stoppedAngle) > .2 &&
                        Math.Abs(pumps.Sum(p => p.ContentAmountLitres) + pipe.ContentAmountLitres - 4) < 1e-10,
                        "Opening the blocked outlet restarts the common shaft and conserves the shared assembly's fluid");
                }
            }
            finally { timing.Dispose(); }
        }

        entities.Clear(); entities[driveEntity.Pos] = driveEntity;
        TestDevice other = new() { Api = api, Pos = new BlockPos(0, -1, 0) };
        entities[other.Pos] = other;
        check(drive.HasAttachedDevice && !drive.HasAttachedPump && Math.Abs(drive.GetResistance() - .2515) < 1e-6,
            "A non-pump device uses the same attachment, centering and load interface");
    }

    private sealed class FixtureEntity : BlockEntity { }
    private sealed class TestDevice : BlockEntity, IReciprocatingDriveDevice
    {
        public BlockPos Position => Pos;
        public BlockFacing DriveFace => BlockFacing.UP;
        public BlockFacing ShaftFace => BlockFacing.SOUTH;
        public float SampleReciprocatingLoad(double angle, double travel, double seconds) => .25f;
        public void StepReciprocatingDrive(double seconds) { }
    }
}
