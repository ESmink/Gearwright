using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Mechanics;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>
/// Server-authoritative pipe storage and pressure simulation. Every step is planned
/// from an immutable snapshot and balanced across connected edges before commit.
/// </summary>
public sealed partial class HydraulicNetworkSystem : ModSystem
{
    private const int SimulationIntervalMilliseconds = 20;
    private const double SimulationStepSeconds = SimulationIntervalMilliseconds / 1000.0;
    private const double MaximumCatchUpHours = 24 * 365;
    private const double EmptyEpsilonLitres = 0.000001;

    private readonly Dictionary<BlockPos, IHydraulicNetworkNode> loadedNodes = new();
    private readonly Dictionary<BlockPos, BlockEntityReciprocatingPump> loadedPumps = new();
    private ICoreServerAPI? sapi;
    private long tickListenerId;
    private int simulationTick;
    private readonly List<NetworkComponent> components = new();
    private bool topologyDirty = true;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        tickListenerId = api.Event.RegisterGameTickListener(
            OnServerTick, SimulationIntervalMilliseconds, SimulationIntervalMilliseconds / 2);
    }

    public void Register(IHydraulicNetworkNode node) { loadedNodes[node.Position.Copy()] = node; topologyDirty = true; }

    public void Unregister(IHydraulicNetworkNode node) { loadedNodes.Remove(node.Position); topologyDirty = true; }

    public void RegisterPump(BlockEntityReciprocatingPump pump)
    {
        loadedPumps[pump.Pos.Copy()] = pump;
        pump.UsesNetworkSolver = true;
    }

    public void UnregisterPump(BlockEntityReciprocatingPump pump)
    {
        loadedPumps.Remove(pump.Pos);
        pump.UsesNetworkSolver = false;
    }

    public override void Dispose()
    {
        if (sapi != null && tickListenerId != 0) sapi.Event.UnregisterGameTickListener(tickListenerId);
        loadedNodes.Clear();
        foreach (var pump in loadedPumps.Values) pump.UsesNetworkSolver = false;
        loadedPumps.Clear();
        components.Clear();
        sapi = null;
    }

    public bool WouldPlacementJoinDifferentContents(BlockPos position, BlockFacing? onlyFace = null)
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        HashSet<IHydraulicNetworkNode> visited = new();
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (onlyFace != null && face != onlyFace) continue;
            if (!loadedNodes.TryGetValue(position.AddCopy(face), out IHydraulicNetworkNode? adjacent) ||
                !adjacent.CanConnect(face.Opposite) || !visited.Add(adjacent)) continue;
            CollectFilledContentCodes(adjacent, visited, codes);
            if (codes.Count > 1) return true;
        }
        return false;
    }

    public bool WouldJoinDifferentContents(BlockEntityFluidPipe pipe, BlockFacing openingFace)
    {
        if (!loadedNodes.TryGetValue(pipe.Pos.AddCopy(openingFace), out IHydraulicNetworkNode? adjacent) ||
            !adjacent.CanConnect(openingFace.Opposite)) return false;

        HashSet<string> codes = new(StringComparer.Ordinal);
        HashSet<IHydraulicNetworkNode> visited = new() { pipe };
        CollectFilledContentCodes(pipe, visited, codes);
        if (visited.Add(adjacent)) CollectFilledContentCodes(adjacent, visited, codes);
        return codes.Count > 1;
    }

    private void CollectFilledContentCodes(
        IHydraulicNetworkNode seed,
        HashSet<IHydraulicNetworkNode> visited,
        HashSet<string> codes)
    {
        Queue<IHydraulicNetworkNode> pending = new();
        pending.Enqueue(seed);
        while (pending.Count > 0)
        {
            IHydraulicNetworkNode node = pending.Dequeue();
            if (node is BlockEntityFluidPipe { HasContent: true } pipe && pipe.CurrentContentCode != null)
            {
                codes.Add(pipe.CurrentContentCode.ToString());
            }
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!node.CanConnect(face) ||
                    !loadedNodes.TryGetValue(node.Position.AddCopy(face), out IHydraulicNetworkNode? next) ||
                    !next.CanConnect(face.Opposite) || !visited.Add(next)) continue;
                pending.Enqueue(next);
            }
        }
    }

    private void OnServerTick(float _)
    {
        if (sapi == null || (loadedNodes.Count == 0 && loadedPumps.Count == 0)) return;
        BlockEntityReciprocatingPump[] pumps = loadedPumps.Values.ToArray();
        foreach (BlockEntityReciprocatingPump pump in pumps)
        {
            pump.ClearTransferPorts();
            pump.PrepareSimulation(SimulationStepSeconds);
        }
        bool runConsumers = ++simulationTick % 50 == 0;
        if (topologyDirty || simulationTick % 10 == 1)
        {
            topologyDirty = false;
            components.Clear();
            HashSet<IHydraulicNetworkNode> visited = new();
            foreach (IHydraulicNetworkNode seed in loadedNodes.Values)
                if (!visited.Contains(seed)) components.Add(DiscoverComponent(seed, visited));
        }
        foreach (NetworkComponent component in components)
        {
            if (simulationTick < component.NextAttemptTick) continue;
            if (!component.Complete || component.Nodes.Any(node => BlockFacing.ALLFACES.Any(face =>
                node.CanConnect(face) && sapi.World.BlockAccessor.GetChunkAtBlockPos(node.Position.AddCopy(face)) == null)))
            {
                PauseComponent(component, "waiting-for-chunks");
                continue;
            }
            Simulate(component, runConsumers);
        }
        foreach (BlockEntityReciprocatingPump pump in pumps)
        {
            pump.FinishSimulation(
                IsPumpPortConnected(pump, pump.InputFace),
                IsPumpPortConnected(pump, pump.OutputFace));
            pump.AccumulatePresentation(SimulationStepSeconds);
            if (simulationTick % 5 == 0) pump.MarkDirty(false);
        }
        if (simulationTick % 5 == 0) PumpTimingSystem.PublishHydraulicFrames(pumps);
    }

    private bool IsPumpPortConnected(BlockEntityReciprocatingPump pump, BlockFacing face) =>
        sapi?.World.BlockAccessor.GetBlockEntity(pump.Pos.AddCopy(face)) is BlockEntityFluidPipe pipe &&
        pipe.CanConnect(face.Opposite);

    private NetworkComponent DiscoverComponent(
        IHydraulicNetworkNode seed,
        HashSet<IHydraulicNetworkNode> visited)
    {
        Queue<IHydraulicNetworkNode> pending = new();
        List<IHydraulicNetworkNode> nodes = new();
        bool complete = true;
        pending.Enqueue(seed);
        visited.Add(seed);

        while (pending.Count > 0)
        {
            IHydraulicNetworkNode node = pending.Dequeue();
            nodes.Add(node);
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!node.CanConnect(face)) continue;
                BlockPos otherPos = node.Position.AddCopy(face);
                if (sapi!.World.BlockAccessor.GetChunkAtBlockPos(otherPos) == null)
                {
                    complete = false;
                    continue;
                }
                if (!loadedNodes.TryGetValue(otherPos, out IHydraulicNetworkNode? other) ||
                    !other.CanConnect(face.Opposite) || !visited.Add(other)) continue;
                pending.Enqueue(other);
            }
        }
        return new NetworkComponent(nodes, complete);
    }

    private void Simulate(NetworkComponent component, bool runConsumers)
    {
        List<BlockEntityFluidPipe> pipes = component.Nodes.OfType<BlockEntityFluidPipe>()
            .OrderBy(pipe => pipe.Pos.X).ThenBy(pipe => pipe.Pos.Y).ThenBy(pipe => pipe.Pos.Z)
            .ToList();
        if (pipes.Count == 0)
        {
            foreach (BlockEntityPassiveFluidPump legacy in component.Nodes.OfType<BlockEntityPassiveFluidPump>())
                legacy.SetNetworkState(null, 0, "deprecated", null);
            return;
        }

        // A receiver whose original save must remain untouched cannot commit
        // incoming fluid. Pause the whole component before debiting any donor.
        if (pipes.Any(pipe => !pipe.CanWriteState))
        {
            PauseComponent(component, "state-read-only");
            return;
        }
        if (pipes.Count > PipePressureSolver.MaximumCells)
        {
            PauseComponent(component, "network-too-large");
            return;
        }

        if (pipes.Any(pipe => pipe.ContentAmountLitres > 0 &&
                              pipe.CurrentContentCode == null))
        {
            PauseComponent(component, "invalid-content");
            return;
        }

        Dictionary<BlockPos, int> pipeByPosition = pipes.Select((pipe, index) => (pipe, index))
            .ToDictionary(pair => pair.pipe.Pos.Copy(), pair => pair.index);
        List<ReciprocatingPumpPort> reciprocatingPorts = new();
        for (int pipeIndex = 0; pipeIndex < pipes.Count; pipeIndex++)
        foreach (BlockFacing pipeFace in BlockFacing.ALLFACES)
        {
            if (!pipes[pipeIndex].CanConnect(pipeFace) ||
                !loadedPumps.TryGetValue(
                    pipes[pipeIndex].Pos.AddCopy(pipeFace),
                    out BlockEntityReciprocatingPump? pump) ||
                !pump.CanConnect(pipeFace.Opposite)) continue;
            reciprocatingPorts.Add(new ReciprocatingPumpPort(
                pump, pipeFace.Opposite, pipeIndex));
        }
        int activeChambers = reciprocatingPorts.Count(port =>
            port.PumpFace == port.Pump.InputFace && port.Pump.CurrentStroke == ReciprocatingPumpStroke.Suction ||
            port.PumpFace == port.Pump.OutputFace && port.Pump.CurrentStroke == ReciprocatingPumpStroke.Pressure);
        if (pipes.Count + activeChambers > PipePressureSolver.MaximumCells)
        {
            PauseComponent(component, "network-too-large");
            return;
        }
        List<AssetLocation> storedCodes = pipes
            .Where(pipe => pipe.HasContent && pipe.CurrentContentCode != null)
            .Select(pipe => pipe.CurrentContentCode!)
            .Distinct(AssetLocationComparer.Instance)
            .ToList();
        if (storedCodes.Count > 1)
        {
            PauseComponent(component, "mixed-content");
            return;
        }

        List<BlockEntityCreativeFluidPump> creativeSources = component.Nodes
            .OfType<BlockEntityCreativeFluidPump>()
            .OrderBy(pump => pump.Pos.X).ThenBy(pump => pump.Pos.Y).ThenBy(pump => pump.Pos.Z)
            .ToList();
        List<AssetLocation> boundaryCodes = new();
        boundaryCodes.AddRange(creativeSources.Select(source => source.GetOffer())
            .Where(offer => offer.Pressure > 0 && PipeContent.IsValid(sapi!.World, offer.ContentCode))
            .Select(offer => offer.ContentCode));
        boundaryCodes.AddRange(reciprocatingPorts
            .Select(port => port.Pump.BoundaryContentCode(port.PumpFace))
            .Where(code => code != null)
            .Select(code => code!));
        foreach (BlockEntityFluidPipe pipe in pipes)
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.GetAddon(face) != HydraulicFaceAddon.PipeNozzle) continue;
            AssetLocation? sourceCode = GetNozzleSourceCode(pipe, face);
            if (sourceCode != null) boundaryCodes.Add(sourceCode);
        }

        AssetLocation? contentCode = storedCodes.FirstOrDefault();
        if (contentCode == null)
        {
            List<AssetLocation> distinctBoundaries = boundaryCodes
                .Distinct(AssetLocationComparer.Instance).ToList();
            if (distinctBoundaries.Count > 1)
            {
                PauseComponent(component, "mixed-sources");
                return;
            }
            contentCode = distinctBoundaries.FirstOrDefault();
        }
        if (contentCode == null)
        {
            SetEmpty(component.Nodes, pipes);
            return;
        }
        if (!PipeContent.IsValid(sapi!.World, contentCode))
        {
            PauseComponent(component, "invalid-content");
            return;
        }

        PipeContentPhase phase = PipeContent.Phase(sapi.World, contentCode);
        int count = pipes.Count;
        double[] amounts = new double[count];
        double[] temperatures = new double[count];
        double[] energies = new double[count];
        double[] stepPressures = new double[count];
        double[] throughputs = new double[count];
        double[][] nozzleFlows = Enumerable.Range(0, count)
            .Select(_ => new double[BlockFacing.NumberOfFaces]).ToArray();
        double[] strongestFlow = new double[count];
        BlockFacing?[] flowDirections = new BlockFacing?[count];
        double[] lastConsumerHours = pipes.Select(pipe => pipe.LastSimulationTotalHours).ToArray();
        bool boundaryMismatch = false;
        bool gasContainerBlocked = false;

        for (int i = 0; i < count; i++)
        {
            BlockEntityFluidPipe pipe = pipes[i];
            amounts[i] = pipe.CurrentContentCode == null ||
                pipe.CurrentContentCode.Equals(contentCode)
                ? Math.Max(0, pipe.ContentAmountLitres)
                : 0;
            temperatures[i] = amounts[i] > EmptyEpsilonLitres
                ? pipe.ContentTemperatureC
                : PipeContent.DefaultTemperatureC(contentCode);
            energies[i] = amounts[i] * temperatures[i];
        }

        if (!SolvePressureFlow(pipes, pipeByPosition, reciprocatingPorts, creativeSources,
            contentCode, phase, amounts, temperatures, energies, stepPressures, throughputs,
            strongestFlow, flowDirections, nozzleFlows, ref boundaryMismatch, ref gasContainerBlocked))
        {
            PauseComponent(component, "flow-solving");
            return;
        }
        double now = sapi.World.Calendar.TotalHours;
        if (runConsumers)
        {
            for (int i = 0; i < count; i++)
            {
                BlockEntityFluidPipe pipe = pipes[i];
                bool sprinkler = pipe.HasSprinkler;
                bool irrigator = pipe is BlockEntityIrrigatorPipe;
                if (!sprinkler && !irrigator) continue;
                double elapsed = pipe.LastSimulationTotalHours < 0
                    ? 0
                    : Math.Clamp(now - pipe.LastSimulationTotalHours, 0, MaximumCatchUpHours);
                lastConsumerHours[i] = now;
                if (phase != PipeContentPhase.Liquid || amounts[i] <= EmptyEpsilonLitres) continue;
                double litresPerDay = sprinkler
                    ? HydraulicMath.LitresPerDayPerConsumer(stepPressures[i])
                    : HydraulicMath.IrrigatorLitresPerDay(stepPressures[i]);
                double wanted = litresPerDay * elapsed / 24;
                double consumed = Math.Min(wanted, amounts[i]);
                if (consumed <= EmptyEpsilonLitres) continue;
                RemoveAmount(i, consumed, amounts, energies, temperatures);
                if (sprinkler) ApplySprinkler(pipe, contentCode, stepPressures[i]);
                else ApplyIrrigator((BlockEntityIrrigatorPipe)pipe, contentCode, stepPressures[i]);
                RecordFlow(i, consumed / Math.Max(SimulationStepSeconds, elapsed * 3600), BlockFacing.DOWN,
                    throughputs, strongestFlow, flowDirections);
            }
        }

        double totalAmount = amounts.Sum();
        bool networkEmpty = totalAmount == 0;
        string status = networkEmpty
            ? "empty"
            : gasContainerBlocked
                ? "gas-container-blocked"
                : boundaryMismatch
                    ? "content-mismatch"
                    : "running";

        for (int i = 0; i < count; i++)
        {
            if (amounts[i] == 0)
            {
                amounts[i] = 0;
                temperatures[i] = PipeContent.DefaultTemperatureC(contentCode);
            }
            // Commit this derived cache only after every pipe has read the
            // previous step. Baseline bias and gravity never seed driven suction.
            stepPressures[i] = HydraulicMath.StoredPressure(amounts[i], temperatures[i], phase, pipes[i].DrivenSuctionKPa);
            pipes[i].ApplySimulationState(
                networkEmpty ? null : contentCode,
                // A dry, known-content run still carries suction to its source.
                // Clearing it here prevents priming beyond the adjacent pipe.
                amounts[i], temperatures[i], stepPressures[i], status,
                flowDirections[i], throughputs[i], nozzleFlows[i], lastConsumerHours[i]);
        }

        foreach (BlockEntityCreativeFluidPump source in creativeSources)
        {
            PumpOffer offer = source.GetOffer();
            source.SetNetworkState(networkEmpty ? null : contentCode, offer.Pressure,
                offer.ContentCode.Equals(contentCode) ? status : "content-mismatch", null);
        }
        foreach (BlockEntityPassiveFluidPump legacy in component.Nodes.OfType<BlockEntityPassiveFluidPump>())
            legacy.SetNetworkState(null, 0, "deprecated", null);
    }

    private AssetLocation? GetNozzleSourceCode(BlockEntityFluidPipe pipe, BlockFacing face)
    {
        NaturalLiquidSource? natural = GetNaturalLiquidSource(pipe, face);
        if (natural != null) return natural.Value.ContentCode;
        NozzleContainer? found = GetNozzleContainer(pipe, face);
        if (found?.Source == null) return null;
        ItemStack? content = found.Value.Interface.GetContent(found.Value.Position);
        return content is { StackSize: > 0 } &&
            PipeContent.IsVintageStoryLiquid(sapi!.World, content.Collectible.Code)
            ? content.Collectible.Code
            : null;
    }

    private NaturalLiquidSource? GetNaturalLiquidSource(
        BlockEntityFluidPipe pipe,
        BlockFacing face)
    {
        BlockPos position = pipe.Pos.AddCopy(face);
        Block fluid = sapi!.World.BlockAccessor.GetBlock(position, BlockLayersAccess.Fluid);
        if (!fluid.IsLiquid() || fluid.LiquidLevel != 7) return null;
        WaterTightContainableProps? props = fluid.Attributes?["waterTightContainerProps"]
            .AsObject<WaterTightContainableProps>(null, fluid.Code.Domain);
        JsonItemStack? filled = props?.WhenFilled?.Stack;
        AssetLocation? code = filled?.ResolvedItemstack?.Collectible.Code ?? filled?.Code;
        if (code == null || !PipeContent.IsVintageStoryLiquid(sapi.World, code)) return null;
        double temperature = filled?.ResolvedItemstack == null
            ? PipeContent.DefaultTemperatureC(code)
            : filled.ResolvedItemstack.Collectible.GetTemperature(
                sapi.World, filled.ResolvedItemstack);
        return new NaturalLiquidSource(code, temperature);
    }

    private NozzleContainer? GetNozzleContainer(BlockEntityFluidPipe pipe, BlockFacing face)
    {
        BlockPos position = pipe.Pos.AddCopy(face);
        Block block = sapi!.World.BlockAccessor.GetBlock(position);
        if (block is not ILiquidInterface liquid) return null;
        if (sapi.World.BlockAccessor.GetBlockEntity(position) is BlockEntityBarrel { Sealed: true }) return null;
        return new NozzleContainer(
            position, liquid, block as ILiquidSource, block as ILiquidSink);
    }

    private bool IsAtmosphericOutlet(BlockEntityFluidPipe pipe, BlockFacing face)
    {
        if (pipe is BlockEntityIrrigatorPipe) return false;
        if (pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle && GetNaturalLiquidSource(pipe, face) != null) return false;
        if (sapi!.World.BlockAccessor.GetBlock(pipe.Pos.AddCopy(face)).BlockMaterial !=
            EnumBlockMaterial.Air) return false;
        return pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle || pipe.IsPortEnabled(face);
    }

    private static double NozzleWorldY(BlockEntityFluidPipe pipe, BlockFacing face) =>
        pipe.Pos.Y + 0.5 + face.Normali.Y * 0.5;

    private static void RemoveAmount(
        int index,
        double litres,
        double[] amounts,
        double[] energies,
        double[] temperatures)
    {
        double removed = Math.Min(amounts[index], Math.Max(0, litres));
        amounts[index] -= removed;
        energies[index] -= removed * temperatures[index];
        if (amounts[index] == 0)
        {
            amounts[index] = 0;
            energies[index] = 0;
        }
    }

    private static void RecordFlow(
        int index,
        double rate,
        BlockFacing face,
        double[] throughputs,
        double[] strongestFlow,
        BlockFacing?[] flowDirections)
    {
        throughputs[index] += Math.Max(0, rate);
        if (rate <= strongestFlow[index]) return;
        strongestFlow[index] = rate;
        flowDirections[index] = face;
    }

    private void PauseComponent(NetworkComponent component, string status)
    {
        SetFault(component.Nodes, status);
        component.NextAttemptTick = simulationTick + 10;
    }

    private static void SetFault(IEnumerable<IHydraulicNetworkNode> nodes, string status)
    {
        double[] noNozzleFlow = new double[BlockFacing.NumberOfFaces];
        foreach (IHydraulicNetworkNode node in nodes)
        {
            if (node is BlockEntityFluidPipe pipe)
            {
                pipe.DrivenSuctionKPa = 0;
                pipe.ApplySimulationState(
                    pipe.CurrentContentCode, pipe.ContentAmountLitres, pipe.ContentTemperatureC,
                    pipe.CurrentPressure, status, null, 0, noNozzleFlow, pipe.LastSimulationTotalHours);
            }
            else node.SetNetworkState(null, 0, status, null);
        }
    }

    private static void SetEmpty(
        IEnumerable<IHydraulicNetworkNode> nodes,
        IEnumerable<BlockEntityFluidPipe> pipes)
    {
        double[] noNozzleFlow = new double[BlockFacing.NumberOfFaces];
        HashSet<IHydraulicNetworkNode> pipeNodes = pipes.Cast<IHydraulicNetworkNode>().ToHashSet();
        foreach (BlockEntityFluidPipe pipe in pipes)
        {
            pipe.DrivenSuctionKPa = 0;
            pipe.ApplySimulationState(null, 0, 20, 0, "empty", null, 0,
                noNozzleFlow, pipe.LastSimulationTotalHours);
        }
        foreach (IHydraulicNetworkNode node in nodes)
        {
            if (pipeNodes.Contains(node)) continue;
            node.SetNetworkState(null, 0,
                node is BlockEntityPassiveFluidPump ? "deprecated" : "empty", null);
        }
    }

    private void ApplySprinkler(BlockEntityFluidPipe sprinkler, AssetLocation content, double pressure)
    {
        int reach = HydraulicMath.Reach(pressure);
        if (reach <= 0) return;
        bool freshWater = content.Equals(new AssetLocation(HydraulicCodes.FreshWater));
        float targetMoisture = (float)HydraulicMath.Performance(pressure);

        for (int dx = -reach; dx <= reach; dx++)
        for (int dz = -reach; dz <= reach; dz++)
        {
            if (!HydraulicMath.IsInsideCircularReach(dx, dz, reach)) continue;
            BlockEntityFarmland? farmland = null;
            for (int depth = 1; depth <= 4; depth++)
            {
                BlockPos target = sprinkler.Pos.AddCopy(dx, -depth, dz);
                farmland = sapi!.World.BlockAccessor.GetBlockEntity(target) as BlockEntityFarmland;
                if (farmland != null) break;
            }
            if (farmland == null) continue;
            if (freshWater)
            {
                float needed = Math.Max(0, targetMoisture - farmland.MoistureLevel);
                if (needed > 0) farmland.WaterFarmland(needed * 2, false);
            }
            else MarkCropExposure(farmland, content);
        }
    }

    private void ApplyIrrigator(
        BlockEntityIrrigatorPipe irrigator,
        AssetLocation content,
        double pressure)
    {
        float performance = (float)HydraulicMath.IrrigatorPerformance(pressure);
        if (performance <= 0) return;
        bool freshWater = content.Equals(new AssetLocation(HydraulicCodes.FreshWater));
        float targetMoisture = (float)(HydraulicMath.IrrigatorMaximumMoisture * performance);
        (int X, int Z)[] offsets = irrigator.AlongX
            ? new[] { (0, 0), (0, -1), (0, 1) }
            : new[] { (0, 0), (-1, 0), (1, 0) };

        foreach ((int offsetX, int offsetZ) in offsets)
        {
            BlockEntityFarmland? farmland = null;
            for (int depth = 1; depth <= 4; depth++)
            {
                BlockPos target = irrigator.Pos.AddCopy(offsetX, -depth, offsetZ);
                farmland = sapi!.World.BlockAccessor.GetBlockEntity(target) as BlockEntityFarmland;
                if (farmland != null) break;
            }
            if (farmland == null) continue;
            if (freshWater)
            {
                float needed = Math.Max(0, targetMoisture - farmland.MoistureLevel);
                if (needed > 0) farmland.WaterFarmland(needed * 2, false);
            }
            else MarkCropExposure(farmland, content);
        }
    }

    private void MarkCropExposure(BlockEntityFarmland farmland, AssetLocation content)
    {
        Block crop = sapi!.World.BlockAccessor.GetBlock(farmland.UpPos);
        if (crop.CropProps == null) return;
        ITreeAttribute? existing = farmland.CropAttributes.GetTreeAttribute(HydraulicCodes.FarmlandExposureTree);
        int? schema = existing?.TryGetInt("schemaVersion");
        if (existing != null && schema != 1)
        {
            sapi.Logger.Error(
                "[Gearwright] Crop exposure data at {0} has an unsupported schema and was left untouched.",
                farmland.Pos);
            return;
        }
        ITreeAttribute exposure = existing?.Clone() ?? new TreeAttribute();
        exposure.SetInt("schemaVersion", 1);
        exposure.SetString("liquidCode", content.ToString());
        exposure.SetDouble("exposedAtTotalHours", sapi.World.Calendar.TotalHours);
        farmland.CropAttributes[HydraulicCodes.FarmlandExposureTree] = exposure;
        farmland.MarkDirty(false);
    }

    private sealed record ReciprocatingPumpPort(
        BlockEntityReciprocatingPump Pump,
        BlockFacing PumpFace,
        int PipeIndex);
    private sealed record NetworkComponent(List<IHydraulicNetworkNode> Nodes, bool Complete)
    {
        public int NextAttemptTick { get; set; }
    }
    private readonly record struct NozzleContainer(
        BlockPos Position,
        ILiquidInterface Interface,
        ILiquidSource? Source,
        ILiquidSink? Sink);
    private readonly record struct NaturalLiquidSource(
        AssetLocation ContentCode,
        double TemperatureC);

    private sealed class AssetLocationComparer : IEqualityComparer<AssetLocation>
    {
        public static readonly AssetLocationComparer Instance = new();
        public bool Equals(AssetLocation? x, AssetLocation? y) => x?.Equals(y) == true;
        public int GetHashCode(AssetLocation obj) => obj.GetHashCode();
    }
}
