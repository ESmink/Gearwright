using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Stub = Gearwright.Contracts.PumpRuntimeFixture.Stub;

namespace Gearwright.Contracts;

internal static class HydraulicRuntimeFixture
{
    public static void Run(Action<bool, string> check)
    {
        Dictionary<BlockPos, BlockEntity> entities = new();
        Dictionary<BlockPos, Block> blocks = new();
        bool chunksLoaded = true;
        long now = 0;
        Action<float>? tick = null;
        Item water = new() { Code = new("game:waterportion"),
            Attributes = JsonObject.FromJson("{\"waterTightContainerProps\":{\"itemsPerLitre\":100}}") };
        var chunk = Stub.Create<IWorldChunk>((_, _) => null);
        var logger = Stub.Create<ILogger>((_, _) => null);
        var accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetChunkAtBlockPos" => chunksLoaded ? chunk : null,
            "GetBlock" => blocks.GetValueOrDefault((BlockPos)args![0]!) ??
                new Block { BlockMaterial = entities.ContainsKey((BlockPos)args![0]!) ? EnumBlockMaterial.Stone : EnumBlockMaterial.Air },
            _ => null
        });
        var calendar = Stub.Create<IGameCalendar>((_, _) => null);
        var world = Stub.Create<IServerWorldAccessor>((method, _) => method.Name switch
        {
            "get_BlockAccessor" => accessor, "get_Logger" => logger,
            "get_Calendar" => calendar, "get_ElapsedMilliseconds" => now, "GetItem" => water, _ => null
        });
        var events = Stub.Create<IServerEventAPI>((method, args) =>
        {
            if (method.Name == "RegisterGameTickListener") { tick = (Action<float>)args![0]!; return 1L; }
            return null;
        });
        var api = Stub.Create<ICoreServerAPI>((method, _) => method.Name switch
        {
            "get_World" => world, "get_Event" => events, "get_Logger" => logger,
            "get_Side" => EnumAppSide.Server, _ => null
        });
        HydraulicNetworkSystem system = new();
        system.StartServerSide(api);
        List<BlockEntityFluidPipe> pipes = new();
        BlockEntityFluidPipe Pipe(int x, int y, double amount, string code, params BlockFacing[] faces)
        {
            var pipe = new BlockEntityFluidPipe { Api = api, Pos = new(x, y, 0) };
            PumpRuntimeFixture.Set(pipe, "Block", new Block { Code = new("gearwright:fluid-pipe-copper") });
            TreeAttribute tree = new(); tree.SetInt("posx", x); tree.SetInt("posy", y);
            TreeAttribute state = new(); state.SetInt("schemaVersion", 7);
            state.SetString("networkContent", code); state.SetDouble("contentAmountLitres", amount);
            state.SetDouble("contentTemperatureC", 20); state.SetDouble("networkPressure", 1e6);
            state.SetString("future-note", "retain me");
            foreach (var face in faces) state.SetInt("port-" + face.Index, 1);
            tree["gearwrightHydraulics"] = state;
            pipe.FromTreeAttributes(tree, world);
            entities[pipe.Pos] = pipe; pipes.Add(pipe); system.Register(pipe);
            return pipe;
        }
        void Step() { now += 20; tick!(.02f); }
        void Clear()
        {
            foreach (var pipe in pipes) system.Unregister(pipe);
            pipes.Clear(); entities.Clear(); blocks.Clear();
        }
        try
        {
            var upper = Pipe(0, 1, 8, "game:waterportion", BlockFacing.DOWN);
            var lower = Pipe(0, 0, 2, "game:waterportion", BlockFacing.UP);
            Step();
            check(upper.ContentAmountLitres < 8 && lower.ContentAmountLitres > 2 &&
                upper.CurrentPressure < 10 && Math.Abs(upper.ContentAmountLitres + lower.ContentAmountLitres - 10) < 1e-12,
                "Actual pipe simulation uses gravity and replaces stale saved pressure without altering total liquid");
            double retained = upper.ContentAmountLitres;
            chunksLoaded = false; Step();
            check(upper.ContentAmountLitres == retained && upper.NetworkStatusCode == "waiting-for-chunks",
                "A newly unloaded neighboring chunk stops cached pipe transfers immediately");
            chunksLoaded = true;
            for (int i = 0; i < 12; i++) Step();
            check(upper.ContentAmountLitres < retained, "Pipe flow resumes after neighboring chunks load");
            TreeAttribute saved = new(); lower.ToTreeAttributes(saved);
            double stored = lower.ContentAmountLitres;
            lower.FromTreeAttributes(saved, world);
            TreeAttribute roundTrip = new(); lower.ToTreeAttributes(roundTrip);
            check(lower.ContentAmountLitres == stored && roundTrip.GetTreeAttribute("gearwrightHydraulics").GetString("future-note") == "retain me",
                "New pressure simulation keeps schema-7 amounts and unknown fields through actual save/load");
            var state = saved.GetTreeAttribute("gearwrightHydraulics"); state.SetInt("schemaVersion", 99);
            lower.FromTreeAttributes(saved, world);
            retained = upper.ContentAmountLitres; Step();
            lower.ToTreeAttributes(roundTrip);
            check(upper.ContentAmountLitres == retained && upper.NetworkStatusCode == "state-read-only" &&
                Bytes(roundTrip.GetTreeAttribute("gearwrightHydraulics")).SequenceEqual(Bytes(state)),
                "A future pipe schema pauses donors and keeps the original saved tree untouched");
            Clear();

            var supply = new TestContainer(water);
            blocks[new(0, 1, 0)] = supply;
            var containerPipe = Pipe(0, 0, 10, "game:waterportion", BlockFacing.EAST);
            TreeAttribute nozzleTree = new(); containerPipe.ToTreeAttributes(nozzleTree);
            nozzleTree.GetTreeAttribute("gearwrightHydraulics").SetInt("addon-" + BlockFacing.UP.Index, (int)HydraulicFaceAddon.PipeNozzle);
            containerPipe.FromTreeAttributes(nozzleTree, world);
            Pipe(1, 0, 10, "game:waterportion", BlockFacing.WEST, BlockFacing.EAST);
            var brim = Pipe(2, 0, 10, "game:waterportion", BlockFacing.WEST, BlockFacing.UP);
            double containerVented = 0;
            bool containerConserved = true;
            for (int i = 0; i < 500; i++)
            {
                Step();
                containerVented += brim.GetNozzleFlowRate(BlockFacing.UP) * .02;
                containerConserved &= Math.Abs(supply.Litres + pipes.Sum(p => p.ContentAmountLitres) + containerVented - 130) < 1e-9;
            }
            Console.WriteLine($"[MEASURE] container full line: remaining {supply.Litres:F5}, vented {containerVented:F5}, conserved {containerConserved}");
            check(containerConserved && supply.Litres < 80 && containerVented > 20,
                "A gravity-fed container delivers over 20 L through a full three-pipe line in ten seconds and conserves every item");
            Clear();

            var gasA = Pipe(0, 0, 40, "gearwright:steam", BlockFacing.UP);
            var gasB = Pipe(0, 1, 2, "gearwright:steam", BlockFacing.DOWN);
            for (int i = 0; i < 200; i++) Step();
            check(gasA.ContentAmountLitres < 40 && gasB.ContentAmountLitres > 2 &&
                gasA.ContentAmountLitres > 10 && Math.Abs(gasA.ContentAmountLitres + gasB.ContentAmountLitres - 42) < 1e-10,
                "Actual steam pipes exchange compressed standard litres through vertical connections without losing gas");
            Clear();

            // A long dry intake with its natural source one block below the pump.
            var pump = new BlockEntityReciprocatingPump { Api = api, Pos = new(0, 1, 0) };
            PumpRuntimeFixture.Set(pump, "Block", new BlockReciprocatingPump { Code = new("gearwright:reciprocating-pump") });
            entities[pump.Pos] = pump;
            var driveEntity = new TestEntity { Api = api, Pos = new(0, 2, 0) };
            var driveBlock = new BlockLateralCrank { Code = new("gearwright:lateral-crank-we") };
            driveBlock.Variant = new Vintagestory.API.Util.RelaxedReadOnlyDictionary<string, string>(new Dictionary<string, string> { ["rotation"] = "we" });
            PumpRuntimeFixture.Set(driveEntity, "Block", driveBlock);
            var drive = new BEBehaviorMPLateralCrank(driveEntity);
            driveEntity.Behaviors.Add(drive); entities[driveEntity.Pos] = driveEntity;
            PumpRuntimeFixture.Set(drive, "Api", api); PumpRuntimeFixture.Set(drive, "gearedRatio", 1f);
            MechanicalNetwork network = new() { Speed = 1 };
            PumpRuntimeFixture.Set(drive, "network", network);
            network.nodes.Add(driveEntity.Pos, drive);
            system.RegisterPump(pump);
            for (int x = -1; x >= -7; x--)
                Pipe(x, 1, 0, "game:waterportion", BlockFacing.EAST, x == -7 ? BlockFacing.DOWN : BlockFacing.WEST);
            var intake = Pipe(-7, 0, 0, "game:waterportion", BlockFacing.UP);
            TreeAttribute intakeTree = new(); intake.ToTreeAttributes(intakeTree);
            intakeTree.GetTreeAttribute("gearwrightHydraulics").SetInt("addon-" + BlockFacing.WEST.Index, (int)HydraulicFaceAddon.PipeNozzle);
            intake.FromTreeAttributes(intakeTree, world);
            Block source = new() { Code = new("game:water-still-7"), BlockMaterial = EnumBlockMaterial.Air, MatterState = EnumMatterState.Liquid,
                LiquidCode = "water", LiquidLevel = 7, Attributes = JsonObject.FromJson(
                    "{\"waterTightContainerProps\":{\"whenFilled\":{\"stack\":{\"type\":\"item\",\"code\":\"game:waterportion\"}}}}") };
            blocks[new(-8, 0, 0)] = source;
            var output = Pipe(1, 1, 0, "game:waterportion", BlockFacing.WEST, BlockFacing.EAST);
            double drawn = 0, discharged = 0, peakFill = 0;
            bool balanced = true, solved = true;
            for (int i = 0; i < 3000; i++)
            {
                network.AngleRad = i * .025f;
                Step();
                drawn -= intake.GetNozzleFlowRate(BlockFacing.WEST) * .02;
                discharged += output.GetNozzleFlowRate(BlockFacing.EAST) * .02;
                peakFill = Math.Max(peakFill, pump.ContentAmountLitres);
                balanced &= Math.Abs(drawn - discharged - pipes.Sum(p => p.ContentAmountLitres) - pump.ContentAmountLitres) < 1e-8;
                solved &= pipes.All(p => p.NetworkStatusCode != "flow-solving");
            }
            Console.WriteLine($"[MEASURE] actual dry lifted intake: drawn {drawn:F3} L, delivered {discharged:F3} L, peak chamber {peakFill:F3} L");
            check(solved && balanced && drawn > 1 && peakFill > 3.9 && discharged > .1 && source.LiquidLevel == 7,
                "A real pump primes a long dry intake with one-block lift, delivers water and conserves all extracted litres");

            MechanicalPowerMod manager = new();
            PumpRuntimeFixture.Set(manager, "serverNwChannel", Stub.Create<IServerNetworkChannel>((_, _) => null));
            PumpRuntimeFixture.Set(network, "mechanicalPowerMod", manager);
            int motorDirection = 1;
            network.nodes.Add(new(2, 2, 0), Stub.Create<IMechanicalPowerNode>((method, args) =>
            {
                if (method.Name == "get_GearedRatio") return 1f;
                if (method.Name == "GetTorque")
                { args![2] = 0f; return motorDirection * .065f * Math.Max(0, 1 - motorDirection * network.Speed / .4f); }
                return null;
            }));
            PumpTimingSystem timing = new(); timing.Start(api);
            try
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    double localDirection = direction * Math.Sign(drive.PumpTravel(pump, 1));
                    motorDirection = direction; network.Speed = direction * .2f;
                    network.AngleRad = direction * 3.3f;
                    pump.PrepareSimulation();
                    peakFill = 0;
                    double minimumTopFill = 1, startDelivery = discharged, intakeSpeed = 0;
                    int completedIntakes = 0, intakeSamples = 0;
                    bool upstrokeRetains = true, extraLoad = false;
                    for (int i = 1; i <= 3000; i++)
                    {
                        double beforeAngle = drive.PumpAngle(pump), beforeAmount = pump.ContentAmountLitres;
                        double beforeVolume = ReciprocatingPumpMath.ChamberVolumeLitres(beforeAngle);
                        network.ServerTick(.02f, i);
                        Step();
                        double afterAngle = drive.PumpAngle(pump);
                        drawn -= intake.GetNozzleFlowRate(BlockFacing.WEST) * .02;
                        discharged += output.GetNozzleFlowRate(BlockFacing.EAST) * .02;
                        balanced &= Math.Abs(drawn - discharged - pipes.Sum(p => p.ContentAmountLitres) - pump.ContentAmountLitres) < 1e-8;
                        solved &= pipes.All(p => p.NetworkStatusCode != "flow-solving");
                        peakFill = Math.Max(peakFill, pump.ContentAmountLitres);
                        if (ReciprocatingPumpMath.VolumeDerivative(beforeAngle) * localDirection > 0 &&
                            ReciprocatingPumpMath.VolumeDerivative(afterAngle) * localDirection > 0 &&
                            Math.Abs(afterAngle - beforeAngle) < .1 && pump.ChamberVolumeLitres > beforeVolume)
                        {
                            upstrokeRetains &= pump.ContentAmountLitres >= beforeAmount - 1e-10;
                            intakeSpeed += Math.Abs(network.Speed); intakeSamples++;
                            extraLoad |= pump.IntakeSuctionKPa < -70 && pump.ContentAmountLitres / pump.ChamberVolumeLitres > .5;
                        }
                        if (i > 100 && ReciprocatingPumpMath.VolumeDerivative(beforeAngle) * localDirection > 0 &&
                            ReciprocatingPumpMath.VolumeDerivative(afterAngle) * localDirection < 0 && beforeVolume > 4)
                        { completedIntakes++; minimumTopFill = Math.Min(minimumTopFill, beforeAmount / beforeVolume); }
                    }
                    Console.WriteLine($"[MEASURE] source with actual motor, direction {direction}: intakes {completedIntakes}, minimum fill {minimumTopFill:P2}, intake speed {intakeSpeed / Math.Max(1, intakeSamples):F3}, delivered {discharged - startDelivery:F3} L; solved {solved}, balanced {balanced}, upstroke retains {upstrokeRetains}, loaded {extraLoad}");
                    check(solved && balanced && upstrokeRetains && extraLoad && completedIntakes >= 3 && minimumTopFill > .98 &&
                        discharged - startDelivery > 8 && intakeSpeed / Math.Max(1, intakeSamples) < .35,
                        $"Source suction loads the actual shaft, fills each chamber, delivers only on contraction and conserves liquid, direction {direction}");
                }
            }
            finally { timing.Dispose(); }
            network.AngleRad = 4; Step();
            network.AngleRad = 4.01f; Step();
            check(pump.IntakeSuctionKPa < -70, "A connected natural nozzle enables sustained suction on the rising stroke");
            blocks.Remove(new(-8, 0, 0)); Step();
            check(pump.IntakeSuctionKPa == 0, "Removing the world source clears its derived suction load on the next hydraulic step");
            blocks[new(-8, 0, 0)] = source;
            system.UnregisterPump(pump);
            foreach (var pipe in pipes) { TreeAttribute tree = new(); pipe.ToTreeAttributes(tree); pipe.FromTreeAttributes(tree, world); }
            drawn = 0;
            for (int i = 0; i < 100; i++) { Step(); drawn -= intake.GetNozzleFlowRate(BlockFacing.WEST) * .02; }
            check(drawn == 0, "Reloading an unpowered intake clears driven suction and cannot extract free natural water");

            // Exercise the independent hydraulic tick too: a downstream pipe
            // can carry strong suction from another pump or a connected loop.
            foreach (int direction in new[] { -1, 1 })
            {
                Clear();
                pump = new BlockEntityReciprocatingPump { Api = api, Pos = new(0, 1, 0) };
                PumpRuntimeFixture.Set(pump, "Block", new BlockReciprocatingPump { Code = new("gearwright:reciprocating-pump") });
                TreeAttribute chamberTree = new(); chamberTree.SetInt("posy", 1);
                TreeAttribute chamberState = new(); chamberState.SetInt("schemaVersion", 1);
                chamberState.SetString("contentCode", "game:waterportion");
                chamberState.SetDouble("amountLitres", 2); chamberState.SetDouble("lastVolumeLitres", 4.05);
                chamberTree["gearwrightReciprocatingPump"] = chamberState;
                pump.FromTreeAttributes(chamberTree, world);
                entities[pump.Pos] = pump; entities[driveEntity.Pos] = driveEntity;
                system.RegisterPump(pump);
                output = Pipe(1, 1, 0, "game:waterportion", BlockFacing.WEST, BlockFacing.EAST);
                var vacuumPipe = Pipe(2, 1, 0, "game:waterportion", BlockFacing.WEST);
                network.AngleRad = 0; Step();
                bool contactBound = true;
                for (int degree = 1; degree <= 180; degree++)
                {
                    vacuumPipe.DrivenSuctionKPa = -98;
                    double before = pump.ContentAmountLitres;
                    network.AngleRad = direction * degree * GameMath.DEG2RAD;
                    Step();
                    contactBound &= pump.ContentAmountLitres >= Math.Min(before, pump.ChamberVolumeLitres) - 1e-10 &&
                        Math.Abs(pump.ContentAmountLitres + pipes.Sum(p => p.ContentAmountLitres) - 2) < 1e-8 &&
                        pipes.All(p => p.NetworkStatusCode != "flow-solving");
                    if (degree == 1) check(pump.ContentAmountLitres == 2,
                        "The hydraulic solver retains all liquid at the handle-away apex despite outlet suction");
                }
                check(contactBound && pipes.Sum(p => p.ContentAmountLitres) > 1.9,
                    $"The independent pipe solver discharges only displaced liquid with downstream vacuum, direction {direction}");
                system.UnregisterPump(pump);
            }

            // A charged supply line must fill the chamber on each intake even
            // when the crank turns much faster than the dry-priming circuit.
            foreach (double rps in new[] { .5, 1, 2, 4, 8 })
            foreach (int direction in new[] { 1, -1 })
            {
                double supplyPressure = rps <= 4 ? 20 : 40;
                Clear();
                pump = new BlockEntityReciprocatingPump { Api = api, Pos = new(0, 1, 0) };
                PumpRuntimeFixture.Set(pump, "Block", new BlockReciprocatingPump { Code = new("gearwright:reciprocating-pump") });
                entities[pump.Pos] = pump; entities[driveEntity.Pos] = driveEntity;
                system.RegisterPump(pump);
                for (int x = -1; x >= -3; x--)
                    Pipe(x, 1, 10, "game:waterportion", BlockFacing.EAST, BlockFacing.WEST);
                var pressureSource = new BlockEntityCreativeFluidPump { Api = api, Pos = new(-4, 1, 0) };
                TreeAttribute sourceTree = new(); sourceTree.SetInt("posx", -4); sourceTree.SetInt("posy", 1);
                TreeAttribute sourceState = new(); sourceState.SetInt("schemaVersion", 7);
                sourceState.SetString("contentCode", "game:waterportion"); sourceState.SetDouble("configuredPressure", supplyPressure);
                sourceTree["gearwrightHydraulics"] = sourceState;
                pressureSource.FromTreeAttributes(sourceTree, world);
                entities[pressureSource.Pos] = pressureSource; system.Register(pressureSource);
                output = Pipe(1, 1, 0, "game:waterportion", BlockFacing.WEST, BlockFacing.EAST);
                double previousAmount = 0, previousVolume = 0, minimumTopFill = 1;
                bool intakeSeen = false;
                int filledStrokes = 0;
                solved = true;
                for (int i = 0; i <= (int)Math.Ceiling(12 / rps / .02); i++)
                {
                    network.AngleRad = (float)(direction * (i * .02 * rps % 1) * Math.PI * 2);
                    Step();
                    solved &= pipes.All(p => p.NetworkStatusCode != "flow-solving");
                    if (i * .02 * rps > 3 && intakeSeen &&
                        pump.CurrentStroke == ReciprocatingPumpStroke.Pressure)
                    {
                        minimumTopFill = Math.Min(minimumTopFill, previousAmount / previousVolume);
                        filledStrokes++;
                    }
                    if (pump.CurrentStroke != ReciprocatingPumpStroke.Stationary)
                        intakeSeen = pump.CurrentStroke == ReciprocatingPumpStroke.Suction;
                    previousAmount = pump.ContentAmountLitres; previousVolume = pump.ChamberVolumeLitres;
                }
                Console.WriteLine($"[MEASURE] {supplyPressure} kPa feed, {direction * rps} RPS: minimum intake completion {minimumTopFill:P2}, strokes {filledStrokes}, solved {solved}");
                check(solved && filledStrokes >= 8 && minimumTopFill >= .99,
                    $"A pump fed through three charged pipes at {supplyPressure} kPa fills each intake at {direction * rps} RPS");
                system.UnregisterPump(pump); system.Unregister(pressureSource);
            }
        }
        finally { system.Dispose(); }
    }

    private sealed class TestEntity : BlockEntity { }

    private sealed class TestContainer : Block, ILiquidSource, ILiquidSink
    {
        private ItemStack contents;
        public double Litres => contents.StackSize / 100.0;
        public TestContainer(Item water)
        {
            Code = new("test:container"); BlockMaterial = EnumBlockMaterial.Wood;
            contents = new(water, 10000);
        }
        public bool AllowHeldLiquidTransfer => true;
        public float CapacityLitres => 100;
        public float TransferSizeLitres => 1;
        public float GetCurrentLitres(BlockPos position) => (float)Litres;
        public float GetCurrentLitres(ItemStack stack) => (float)Litres;
        public bool IsFull(BlockPos position) => contents.StackSize >= 10000;
        public bool IsFull(ItemStack stack) => contents.StackSize >= 10000;
        public WaterTightContainableProps GetContentProps(BlockPos position) => new() { ItemsPerLitre = 100 };
        public WaterTightContainableProps GetContentProps(ItemStack stack) => new() { ItemsPerLitre = 100 };
        public ItemStack GetContent(BlockPos position) => contents;
        public ItemStack GetContent(ItemStack stack) => contents;
        public ItemStack TryTakeContent(BlockPos position, int quantity)
        {
            int amount = Math.Min(contents.StackSize, quantity);
            contents.StackSize -= amount;
            return new ItemStack(contents.Item, amount);
        }
        public ItemStack TryTakeContent(ItemStack stack, int quantity) => TryTakeContent(new BlockPos(0), quantity);
        public void SetContent(BlockPos position, ItemStack stack) => contents = stack;
        public void SetContent(ItemStack container, ItemStack stack) => contents = stack;
        public int TryPutLiquid(BlockPos position, ItemStack stack, float litres)
        {
            int amount = Math.Min(10000 - contents.StackSize, Math.Min(stack.StackSize, (int)(litres * 100)));
            contents.StackSize += amount;
            return amount;
        }
        public int TryPutLiquid(ItemStack container, ItemStack stack, float litres) => TryPutLiquid(new BlockPos(0), stack, litres);
    }

    private static byte[] Bytes(ITreeAttribute tree)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        tree.ToBytes(writer);
        return stream.ToArray();
    }
}
