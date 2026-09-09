using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Contracts;

/// <summary>Runs the actual pump, pipe and drive entities, with only world I/O stubbed.</summary>
internal static class PumpRuntimeFixture
{
    public static void Run(Action<bool, string> check)
    {
        Dictionary<BlockPos, BlockEntity> entities = new();
        Item water = new()
        {
            Code = new AssetLocation("game:waterportion"),
            Attributes = JsonObject.FromJson("{\"waterTightContainerProps\":{}}")
        };
        IWorldChunk chunk = Stub.Create<IWorldChunk>((_, _) => null);
        IBlockAccessor accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
        {
            "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
            "GetChunkAtBlockPos" => chunk,
            "GetBlock" => new Block { BlockMaterial = entities.ContainsKey((BlockPos)args![0]!)
                ? EnumBlockMaterial.Stone : EnumBlockMaterial.Air },
            _ => null
        });
        ILogger logger = Stub.Create<ILogger>((_, _) => null);
        IWorldAccessor world = Stub.Create<IWorldAccessor>((method, _) => method.Name switch
        {
            "get_Logger" => logger,
            "get_BlockAccessor" => accessor,
            "GetItem" => water,
            _ => null
        });
        ICoreAPI api = Stub.Create<ICoreAPI>((method, _) => method.Name switch
        {
            "get_World" => world,
            "get_Side" => EnumAppSide.Server,
            _ => null
        });
        BlockEntityReciprocatingPump pump = new() { Api = api, Pos = new BlockPos(0, 0, 0) };
        Set(pump, "Block", new BlockReciprocatingPump { Code = new AssetLocation("gearwright:reciprocating-pump") });
        entities[pump.Pos] = pump;
        LoadPump(pump, world, 8);
        TreeAttribute legacySaved = new(); pump.ToTreeAttributes(legacySaved);
        legacySaved.GetTreeAttribute("gearwrightReciprocatingPump").SetString("future-note", "retain me");
        pump.FromTreeAttributes(legacySaved, world);
        TreeAttribute legacyRoundTrip = new(); pump.ToTreeAttributes(legacyRoundTrip);
        check(pump.ContentAmountLitres == 8 && pump.ChamberVolumeLitres == 4.05 &&
            legacyRoundTrip.GetTreeAttribute("gearwrightReciprocatingPump").GetDouble("amountLitres") == 8 &&
            legacyRoundTrip.GetTreeAttribute("gearwrightReciprocatingPump").GetString("future-note") == "retain me",
            "Reducing the pump chamber to 4 L preserves all 8 L from an older save and its unknown fields");
        LoadPump(pump, world, .8);
        check(Math.Abs(pump.FillFraction - .2) < 1e-12,
            "The same 0.8 L now fills twenty percent of the pump's swept volume");
        LoadPump(pump, world, 1);
        Set(pump, "CurrentStroke", ReciprocatingPumpStroke.Pressure);
        double sent = pump.Provide(water.Code, .9999995);
        check(pump.HasContent && pump.ContentAmountLitres > 0 &&
            Math.Abs(pump.ContentAmountLitres + sent - 1) < 1e-14,
            "Actual pump Provide retains sub-epsilon fluid instead of deleting the remainder");
        TreeAttribute saved = new();
        pump.ToTreeAttributes(saved);
        BlockEntityReciprocatingPump reloaded = new() { Api = api, Pos = pump.Pos.Copy() };
        reloaded.FromTreeAttributes(saved, world);
        check(reloaded.ContentAmountLitres == pump.ContentAmountLitres && reloaded.HasContent,
            "A retained pump droplet survives the real entity save/load path");

        BlockEntityFluidPipe pipe = new() { Api = api, Pos = new BlockPos(1, 0, 0) };
        pipe.ApplySimulationState(water.Code, 10.25, 20, 100, "running", null, 0, new double[6], 0);
        check(pipe.ContentAmountLitres == 10.25,
            "Actual pipe storage retains an existing over-capacity amount instead of trimming it");
        pipe.ApplySimulationState(water.Code, .0000005, 20, 0, "running", null, 0, new double[6], 0);
        check(pipe.HasContent, "Pipe content identity is retained even for sub-epsilon amounts");

        TestEntity driveEntity = new() { Api = api, Pos = new BlockPos(0, 1, 0) };
        BlockLateralCrank driveBlock = new() { Code = new AssetLocation("gearwright:lateral-crank-we") };
        driveBlock.Variant = new Vintagestory.API.Util.RelaxedReadOnlyDictionary<string, string>(
            new Dictionary<string, string> { ["rotation"] = "we" });
        Set(driveEntity, "Block", driveBlock);
        BEBehaviorMPLateralCrank drive = new(driveEntity);
        driveEntity.Behaviors.Add(drive);
        Set(drive, "Api", api);
        Set(drive, "gearedRatio", 1f);
        entities[driveEntity.Pos] = driveEntity;

        MechanicalNetwork network = new() { Speed = 1, AngleRad = 0 };
        Set(drive, "network", network);
        network.nodes.Add(driveEntity.Pos, drive);

        foreach (int direction in new[] { -1, 1 })
            foreach (string content in new[] { "game:waterportion", "gearwright:steam" })
            {
                LoadPump(pump, world, 4, content);
                float peak = 0;
                bool conserved = true;
                // Force full rotations, even through an overload: pressure and load
                // may change, but chamber volume must never trim stored contents.
                for (int frame = 0; frame < 1440; frame++)
                {
                    network.AngleRad = (float)(direction * frame * Math.PI / 180);
                    pump.PrepareSimulation();
                    pump.FinishSimulation(inputConnected: false, outputConnected: false);
                    float resistance = drive.GetResistance();
                    peak = Math.Max(peak, resistance);
                    conserved &= pump.ContentAmountLitres == 4 && pump.CurrentContentCode?.ToString() == content &&
                        float.IsFinite(resistance) && double.IsFinite(pump.ChamberPressureKPa);
                }
                check(conserved && peak > 1.2,
                    $"Actual blocked pump retains all {content} through repeated forced rotations with finite rising load, direction {direction}");
            }

        // A stationary, pressurized pump can evacuate after an outlet opens.
        LoadPump(pump, world, 2);
        network.AngleRad = 2;
        pump.PrepareSimulation();
        pump.PrepareSimulation();
        float loadedResistance = drive.GetResistance();
        entities[pipe.Pos] = pipe;
        TreeAttribute pipeTree = new();
        pipeTree.SetInt("posx", 1);
        TreeAttribute pipeState = new();
        pipeState.SetInt("schemaVersion", 7);
        pipeState.SetInt("port-" + BlockFacing.WEST.Index, 1);
        pipeTree["gearwrightHydraulics"] = pipeState;
        pipeState.SetDouble("networkPressure", -100);
        pipe.DrivenSuctionKPa = -100;
        pipe.FromTreeAttributes(pipeTree, world);
        check(pipe.DrivenSuctionKPa == 0 && pipe.CurrentPressure == -100,
            "Loading a pipe discards derived suction even if an old pressure cache was artificially negative");
        double accepted = pump.Provide(water.Code, 1);
        pipe.ApplySimulationState(water.Code, accepted, 20, 0, "running", null, 0, new double[6], 0);
        check(accepted == 1 && pump.ContentAmountLitres + pipe.ContentAmountLitres == 2 &&
            drive.GetResistance() < loadedResistance,
            "A stationary pressurized pump can drain and reduce its load without a fresh stroke or fluid loss");

        TreeAttribute compressedSave = new();
        pump.ToTreeAttributes(compressedSave);
        reloaded.FromTreeAttributes(compressedSave, world);
        check(reloaded.ContentAmountLitres == 1 && reloaded.ChamberPressureKPa == pump.ChamberPressureKPa,
            "Save/load and client state reads preserve compressed contents and recompute the displayed pressure");

        MechanicalPowerMod manager = new();
        Set(manager, "serverNwChannel", Stub.Create<IServerNetworkChannel>((_, _) => null));
        Set(network, "mechanicalPowerMod", manager);
        float motorTorque = .1f;
        int torqueCalls = 0;
        network.nodes.Add(new BlockPos(2, 1, 0), Stub.Create<IMechanicalPowerNode>((method, args) =>
        {
            if (method.Name == "get_GearedRatio") return 1f;
            if (method.Name == "GetTorque") { torqueCalls++; args![2] = 0f; return motorTorque; }
            return null;
        }));
        PumpTimingSystem timing = new();
        timing.Start(api);
        try
        {
            foreach (int direction in new[] { -1, 1 })
            {
                motorTorque = direction * .1f;
                LoadPump(pump, world, 4);
                network.AngleRad = direction * .8f;
                network.Speed = direction * .6f;
                pipe.ApplySimulationState(water.Code, 10, 20, 0, "running", null, 0, new double[6], 0);
                pump.SetTransferPort(pump.OutputFace, pipe, 0);
                torqueCalls = 0;
                bool conserved = true;
                for (int tick = 1; tick <= 500; tick++)
                {
                    network.ServerTick(.02f, tick);
                    conserved &= pump.ContentAmountLitres == 4 && pipe.ContentAmountLitres == 10 &&
                        float.IsFinite(network.Speed) && double.IsFinite(pump.ChamberPressureKPa);
                }
                check(conserved && Math.Abs(network.AngleRad) < Math.PI && Math.Abs(network.Speed) < .01 && torqueCalls == 100,
                    $"Actual engine stalls a blocked pump before bottom dead centre without fluid loss; keeps vanilla torque cadence, direction {direction}");

                double stoppedAngle = network.AngleRad;
                pipe.ApplySimulationState(null, 0, 20, 0, "running", null, 0, new double[6], 0);
                bool balanced = true;
                for (int tick = 501; tick <= 650; tick++)
                {
                    network.ServerTick(.02f, tick);
                    balanced &= Math.Abs(pump.ContentAmountLitres + pipe.ContentAmountLitres - 4) < 1e-12;
                }
                check(balanced && pipe.ContentAmountLitres > 3 &&
                      Math.Abs(network.AngleRad - stoppedAngle) > .5 && torqueCalls == 130,
                    $"Unblocking the real pump transfers stored liquid and restarts the engine without a hard lock, direction {direction}");
            }

            foreach (int extraNodes in new[] { 14, 126 })
            {
                for (int node = 0; node < extraNodes; node++)
                    network.nodes.Add(new BlockPos(node + 3, 1, 0), Stub.Create<IMechanicalPowerNode>((method, args) =>
                    {
                        if (method.Name == "get_GearedRatio") return 1f;
                        if (method.Name == "GetTorque") { args![2] = .0001f; return 0f; }
                        return null;
                    }));
                foreach (int direction in new[] { -1, 1 })
                    foreach (float ratio in new[] { 1f, 4f })
                        foreach (double amount in new[] { .1, 4.0 })
                        foreach (float tickSeconds in new[] { .02f, .1f })
                        {
                            motorTorque = direction * .1f;
                            Set(drive, "gearedRatio", ratio);
                            LoadPump(pump, world, amount);
                            network.AngleRad = direction * .8f / ratio;
                            network.Speed = direction * .6f;
                            pipe.ApplySimulationState(water.Code, 10, 20, 0, "running", null, 0, new double[6], 0);
                            pump.SetTransferPort(pump.OutputFace, pipe, 0);
                            bool crossed = false;
                            bool stable = true;
                            int tickStride = (int)Math.Round(tickSeconds * 50);
                            double settledAngle = 0;
                            for (int tick = 5; tick <= 1000; tick += tickStride)
                            {
                                network.ServerTick(tickSeconds, tick);
                                crossed |= Math.Abs(network.AngleRad * ratio) >= Math.PI;
                                stable &= float.IsFinite(network.Speed) && network.Speed * direction >= 0 &&
                                    pump.ContentAmountLitres == amount && pipe.ContentAmountLitres == 10;
                                if (tick == 500) settledAngle = network.AngleRad;
                            }
                            check(!crossed && stable && Math.Abs(network.Speed) < .001 &&
                                  Math.Abs((network.AngleRad - settledAngle) * ratio) < .001,
                                $"Blocked {amount} L pump holds its first downstroke with {network.nodes.Count} nodes, ratio {ratio}, direction {direction}, {tickSeconds * 1000:F0} ms ticks (angle {Math.Abs(network.AngleRad * ratio) * 180 / Math.PI:F2} degrees)");

                            pipe.ApplySimulationState(null, 0, 20, 0, "running", null, 0, new double[6], 0);
                            for (int tick = 1000 + tickStride; tick <= 1250; tick += tickStride)
                            {
                                network.ServerTick(tickSeconds, tick);
                                stable &= Math.Abs(pump.ContentAmountLitres + pipe.ContentAmountLitres - amount) < 1e-12;
                            }
                            check(stable && pipe.ContentAmountLitres > .001 &&
                                  Math.Abs((network.AngleRad - settledAngle) * ratio) > .2,
                                "Releasing the blocked geared pump restarts movement and conserves the trapped liquid");
                        }
                for (int node = 0; node < extraNodes; node++)
                    network.nodes.Remove(new BlockPos(node + 3, 1, 0));
            }
            Set(drive, "gearedRatio", 1f);

            foreach (int direction in new[] { -1, 1 })
            {
                // Above even the numerical load ceiling, real drive torque must
                // win. A fixed angle clamp would incorrectly fail this case.
                motorTorque = direction * 2000000f;
                LoadPump(pump, world, 4);
                network.AngleRad = direction * .8f;
                network.Speed = direction * .6f;
                pipe.ApplySimulationState(water.Code, 10, 20, 0, "running", null, 0, new double[6], 0);
                pump.SetTransferPort(pump.OutputFace, pipe, 0);
                for (int tick = 5; tick <= 100; tick++) network.ServerTick(.02f, tick);
                check(Math.Abs(network.AngleRad) > 2 * Math.PI && pump.ContentAmountLitres == 4 &&
                      pipe.ContentAmountLitres == 10 && float.IsFinite(network.Speed),
                    $"Sufficient torque drives through finite pump resistance without an angle lock or fluid loss, direction {direction}");

                motorTorque = direction * .1f;
                LoadPump(pump, world, 4);
                network.AngleRad = direction * (MathF.PI + .2f);
                network.Speed = 0;
                for (int tick = 5; tick <= 50; tick++) network.ServerTick(.02f, tick);
                check(Math.Abs(network.AngleRad) > Math.PI + .3 && network.Speed * direction > 0,
                    $"A stationary compressed pump can start in the expansion direction, direction {direction}");
            }

            network.AngleRad = 190 * GameMath.DEG2RAD;
            Set(network, "firstTick", false);
            network.UpdateFromPacket(new MechNetworkPacket { angle = 100 * GameMath.DEG2RAD, speed = .000001f }, false);
            for (int i = 0; i < 600; i++) network.ClientTick(1f / 60);
            check(Math.Abs(network.AngleRad - 100 * GameMath.DEG2RAD) < 1e-6,
                "The actual vanilla stopped-client early return no longer strands the shaft at its previous overshoot angle");

            MechanicalNetwork ordinary = new() { AngleRad = 1, Speed = .5f };
            ordinary.ServerTick(.02f, 1);
            check(Math.Abs(ordinary.AngleRad - 1.05) < 1e-6 && ordinary.Speed == .5f,
                "Networks without Gearwright pumps retain the vanilla mechanical tick");

            BlockEntityFluidPipe inlet = new() { Api = api, Pos = new BlockPos(-1, 0, 0) };
            TreeAttribute inletTree = new();
            inletTree.SetInt("posx", -1);
            TreeAttribute inletState = new();
            inletState.SetInt("schemaVersion", 7);
            inletState.SetInt("port-" + BlockFacing.EAST.Index, 1);
            inletTree["gearwrightHydraulics"] = inletState;
            inlet.FromTreeAttributes(inletTree, world);
            entities[inlet.Pos] = inlet;
            foreach (string content in new[] { "game:waterportion", "gearwright:steam" })
                foreach (int direction in new[] { -1, 1 })
                {
                    AssetLocation code = new(content);
                    LoadPump(pump, world, 0, content);
                    inlet.ApplySimulationState(code, 10, 20, 0, "running", null, 0, new double[6], 0);
                    pipe.ApplySimulationState(null, 0, 20, 0, "running", null, 0, new double[6], 0);
                    pump.SetTransferPort(pump.InputFace, inlet, 0);
                    pump.SetTransferPort(pump.OutputFace, pipe, 0);
                    check(inlet.CanConnect(BlockFacing.EAST) && inlet.CanWriteState &&
                          ReferenceEquals(accessor.GetBlockEntity(pump.Pos.AddCopy(pump.InputFace)), inlet),
                        "Transfer fixture connects the loaded inlet to the pump input");
                    bool balanced = true, contact = true;
                    for (int step = 0; step < 720; step++)
                    {
                        network.AngleRad = (float)(direction * step * Math.PI / 180);
                        double before = pump.ContentAmountLitres;
                        double oldOutput = pipe.ContentAmountLitres;
                        pump.StepDrivenPump(.02);
                        balanced &= Math.Abs(inlet.ContentAmountLitres + pump.ContentAmountLitres + pipe.ContentAmountLitres - 10) < 1e-11;
                        if (content != "gearwright:steam" && pump.ChamberVolumeLitres >= before)
                            contact &= oldOutput == pipe.ContentAmountLitres;
                    }
                    check(balanced && contact && pipe.ContentAmountLitres > 1,
                        $"Actual substep intake/exhaust conserves {content} and liquid never evacuates before contact, direction {direction}");
                }

            LoadPump(pump, world, 4);
            network.AngleRad = 2;
            pipe.ApplySimulationState(null, 0, 20, 0, "running", null, 0, new double[6], 0);
            Set(pipe, "canWrite", false);
            pump.StepDrivenPump(.02);
            pump.StepDrivenPump(.02);
            check(pump.ContentAmountLitres == 4 && pipe.ContentAmountLitres == 0,
                "A read-only receiver is rejected before a mechanical substep debits the pump");
            Set(pipe, "canWrite", true);

            LoadPump(pump, world, 4);
            network.AngleRad = 2;
            pipeState.SetInt("port-" + BlockFacing.UP.Index, 1);
            pipe.FromTreeAttributes(pipeTree, world);
            pipe.ApplySimulationState(water.Code, 10, 20, 0, "running", null, 0, new double[6], 0);
            pump.SetTransferPort(pump.OutputFace, pipe, 0);
            pump.StepDrivenPump(.02);
            pump.StepDrivenPump(.02);
            check(pipe.ContentAmountLitres > 10 && Math.Abs(pipe.ContentAmountLitres + pump.ContentAmountLitres - 14) < 1e-12,
                "Mechanical substeps preserve the upward overflow outlet instead of treating its full pipe as sealed");

            LoadPump(pump, world, 4);
            pump.CapturePresentation(drive);
            pump.StepDrivenPump(.0002);
            PumpPresentation rapidTransfer = pump.CapturePresentation(drive);
            check(rapidTransfer.OutputOpen && pump.ContentAmountLitres < 4 &&
                  Math.Abs(rapidTransfer.Throughput * .0002 - (4 - pump.ContentAmountLitres)) < 1e-12,
                "Very short geared substeps report the complete transfer on the shared presentation clock");

            RunHydraulicNetwork(check, world, accessor, entities, pump, network, torque => motorTorque = torque);
            RunClientClock(check, world, accessor, entities, pump, drive, network);
        }
        finally { timing.Dispose(); }
    }

    private static void RunHydraulicNetwork(Action<bool, string> check, IWorldAccessor world,
        IBlockAccessor accessor, Dictionary<BlockPos, BlockEntity> entities,
        BlockEntityReciprocatingPump pump, MechanicalNetwork network, Action<float> setTorque)
    {
        Action<float>? tickHydraulics = null;
        long now = 0;
        IServerEventAPI events = Stub.Create<IServerEventAPI>((method, args) =>
        {
            if (method.Name == "RegisterGameTickListener") { tickHydraulics = (Action<float>)args![0]!; return 1L; }
            return null;
        });
        IGameCalendar calendar = Stub.Create<IGameCalendar>((_, _) => null);
        IServerWorldAccessor serverWorld = Stub.Create<IServerWorldAccessor>((method, _) => method.Name switch
        {
            "get_Logger" => world.Logger,
            "get_BlockAccessor" => accessor,
            "get_Calendar" => calendar,
            "GetItem" => world.GetItem(new AssetLocation("game:waterportion")),
            "get_ElapsedMilliseconds" => now,
            _ => null
        });
        ICoreServerAPI serverApi = Stub.Create<ICoreServerAPI>((method, _) => method.Name switch
        {
            "get_World" => serverWorld,
            "get_Event" => events,
            "get_Side" => EnumAppSide.Server,
            _ => null
        });
        var originals = new Dictionary<BlockPos, BlockEntity>(entities);
        List<BlockEntityFluidPipe> run = new();
        HydraulicNetworkSystem hydraulics = new();
        hydraulics.StartServerSide(serverApi);
        try
        {
            foreach (var scenario in new[] { (Capped: false, Ratio: 1f, Seconds: .02f),
                         (Capped: true, Ratio: 1f, Seconds: .02f), (Capped: true, Ratio: 4f, Seconds: .1f) })
            foreach (int direction in new[] { -1, 1 })
            {
                bool capped = scenario.Capped;
                Set(network.nodes.Values.OfType<BEBehaviorMPLateralCrank>().First(), "gearedRatio", scenario.Ratio);
                foreach (var old in run) hydraulics.Unregister(old);
                run.Clear();
                foreach (BlockPos position in entities.Keys.Where(p => p.Y == 0 && p.X != 0).ToArray()) entities.Remove(position);
                for (int i = 1; i <= 3; i++)
                {
                    var pipe = new BlockEntityFluidPipe { Api = serverApi, Pos = new BlockPos(i, 0, 0) };
                    TreeAttribute tree = new(); tree.SetInt("posx", i);
                    TreeAttribute state = new();
                    state.SetInt("schemaVersion", 7);
                    state.SetInt("port-" + BlockFacing.WEST.Index, 1);
                    if (i < 3) state.SetInt("port-" + BlockFacing.EAST.Index, 1);
                    if (i == 3 && !capped) state.SetInt("port-" + BlockFacing.UP.Index, 1);
                    state.SetDouble("contentAmountLitres", 10);
                    state.SetString("networkContent", "game:waterportion");
                    state.SetDouble("contentTemperatureC", 20);
                    tree["gearwrightHydraulics"] = state;
                    pipe.FromTreeAttributes(tree, serverWorld);
                    entities[pipe.Pos] = pipe;
                    run.Add(pipe);
                    hydraulics.Register(pipe);
                }
                LoadPump(pump, world, 2);
                hydraulics.RegisterPump(pump);
                setTorque(direction * .3f);
                network.AngleRad = direction * .8f / scenario.Ratio;
                network.Speed = direction * .6f;
                double vented = 0, peakPressure = 0;
                bool balance = true, stalledOpen = false;
                int solveFailures = 0;
                for (int tick = 5; tick <= 500; tick++)
                {
                    now += (long)(scenario.Seconds * 1000);
                    network.ServerTick(scenario.Seconds, tick);
                    tickHydraulics!(.02f);
                    if (run[0].NetworkStatusCode == "flow-solving")
                    {
                        if (solveFailures++ == 0) Console.WriteLine($"[MEASURE] first rejected solve: tick {tick}, pump {pump.ContentAmountLitres:R} L / {pump.ChamberVolumeLitres:R} L, pipes {string.Join(",", run.Select(p => p.ContentAmountLitres.ToString("R")))}");
                    }
                    vented += run[^1].GetNozzleFlowRate(BlockFacing.UP) * .02;
                    balance &= Math.Abs(pump.ContentAmountLitres + run.Sum(p => p.ContentAmountLitres) + vented - 32) < 1e-9;
                    if (Math.Abs(network.AngleRad * scenario.Ratio) < Math.PI)
                    {
                        peakPressure = Math.Max(peakPressure, pump.ChamberPressureKPa);
                        stalledOpen |= Math.Abs(network.Speed) < .001;
                    }
                }
                Console.WriteLine($"[MEASURE] actual full line, capped={capped}, direction={direction}, ratio={scenario.Ratio}, dt={scenario.Seconds}: vented {vented:F4} L, peak {peakPressure:F1} kPa, angle {network.AngleRad * scenario.Ratio:F3}, rejected {solveFailures}");
                check(balance && solveFailures == 0 && (capped ? vented == 0 && Math.Abs(network.AngleRad * scenario.Ratio) < Math.PI && Math.Abs(network.Speed) < .001
                    : vented > 1.5 && !stalledOpen && peakPressure < ReciprocatingPumpMath.ServicePressureKPa),
                    $"Actual coupled pump and full pipe line {(capped ? "stalls against a cap" : "discharges without false stalls")} and conserves liquid, direction {direction}, ratio {scenario.Ratio}, {scenario.Seconds * 1000:F0} ms ticks");
            }
        }
        finally
        {
            Set(network.nodes.Values.OfType<BEBehaviorMPLateralCrank>().First(), "gearedRatio", 1f);
            hydraulics.Dispose();
            entities.Clear();
            foreach (var pair in originals) entities[pair.Key] = pair.Value;
        }
    }

    private static void RunClientClock(Action<bool, string> check, IWorldAccessor world,
        IBlockAccessor accessor, Dictionary<BlockPos, BlockEntity> entities,
        BlockEntityReciprocatingPump pump, BEBehaviorMPLateralCrank drive, MechanicalNetwork network)
    {
        long now = 100;
        Delegate? receiver = null;
        IClientNetworkChannel? channel = null;
        channel = Stub.Create<IClientNetworkChannel>((method, args) =>
        {
            if (method.Name == "SetMessageHandler") receiver = (Delegate)args![0]!;
            return channel;
        });
        IClientNetworkAPI networking = Stub.Create<IClientNetworkAPI>((_, _) => channel);
        IClientWorldAccessor clientWorld = Stub.Create<IClientWorldAccessor>((method, _) => method.Name switch
        {
            "get_BlockAccessor" => accessor,
            "get_ElapsedMilliseconds" => now,
            _ => null
        });
        ICoreClientAPI clientApi = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
        {
            "get_World" => clientWorld,
            "get_Side" => EnumAppSide.Client,
            "get_Network" => networking,
            _ => null
        });
        PumpTimingSystem clientTiming = new();
        clientTiming.Start(clientApi);
        clientTiming.StartClientSide(clientApi);
        try
        {
            PumpNetworkFrame Frame(float angle, long seq, float speed)
            {
                network.AngleRad = angle;
                network.Speed = speed;
                LoadPump(pump, world, 4);
                pump.PrepareSimulation();
                return new PumpNetworkFrame
                {
                    NetworkId = network.networkId,
                    Sequence = seq,
                    ServerMilliseconds = seq * 100,
                    Angle = angle,
                    Speed = speed,
                    Pumps = new[] { pump.CapturePresentation(drive) }
                };
            }
            receiver!.DynamicInvoke(Frame(1, 1, 1));
            now = 200;
            receiver.DynamicInvoke(Frame(1.5f, 2, 1));
            now = 250;
            network.ClientTick(1f / 60);
            check(Math.Abs(network.AngleRad - 1.25) < 1e-6 &&
                  PumpTimingSystem.TryPresentation(pump, out var shown) &&
                  Math.Abs(shown.Angle - drive.PumpAngle(pump)) < 1e-6 && shown.Amount == 4,
                "The real client hook puts the engine axle and pump renderer on the same delayed frame");
            now = 300;
            receiver.DynamicInvoke(Frame(1.3f, 3, .000001f));
            network.ClientTick(1f / 60);
            check(Math.Abs(network.AngleRad - 1.3) < 1e-6 &&
                  PumpTimingSystem.TryPresentation(pump, out shown) &&
                  Math.Abs(shown.Angle - drive.PumpAngle(pump)) < 1e-6 &&
                  shown.Stroke == (int)ReciprocatingPumpStroke.Stationary,
                "The client hook snaps both mechanisms on stall and closes non-moving air checks");
            now = 400;
            receiver.DynamicInvoke(Frame(1.4f, 4, 1));
            now = 500;
            network.ClientTick(1f / 60);
            entities.Remove(pump.Pos);
            network.ClientTick(1f / 60);
            check(!PumpTimingSystem.TryPresentation(pump, out _) && Math.Abs(network.AngleRad - 1.4) > 1e-5,
                "Removing the last pump drops its presentation clock and leaves the original shaft free to turn");
        }
        finally
        {
            clientTiming.Dispose();
            entities[pump.Pos] = pump;
        }
    }

    private static void LoadPump(BlockEntityReciprocatingPump pump, IWorldAccessor world, double amount,
        string content = "game:waterportion")
    {
        TreeAttribute tree = new();
        TreeAttribute state = new();
        state.SetInt("schemaVersion", 1);
        state.SetString("contentCode", content);
        state.SetDouble("amountLitres", amount);
        state.SetDouble("lastVolumeLitres", 8.05);
        tree["gearwrightReciprocatingPump"] = state;
        pump.FromTreeAttributes(tree, world);
    }

    internal static void Set(object target, string name, object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(name, flags);
            if (field != null) { field.SetValue(target, value); return; }
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property?.SetMethod != null) { property.SetValue(target, value); return; }
        }
        throw new MissingMemberException(target.GetType().FullName, name);
    }

    private sealed class TestEntity : BlockEntity { }

    public class Stub : DispatchProxy
    {
        private System.Func<MethodInfo, object?[]?, object?> handler = null!;
        public static T Create<T>(System.Func<MethodInfo, object?[]?, object?> handler) where T : class
        {
            T value = Create<T, Stub>();
            ((Stub)(object)value).handler = handler;
            return value;
        }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            object? result = handler(targetMethod!, args);
            Type resultType = targetMethod!.ReturnType;
            return result ?? (resultType != typeof(void) && resultType.IsValueType ? Activator.CreateInstance(resultType) : null);
        }
    }
}
