using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>
/// Server-authoritative pipe storage and pressure simulation. Every step is planned
/// from an immutable snapshot, then scaled and committed without node-order bias.
/// </summary>
public sealed class HydraulicNetworkSystem : ModSystem
{
    private const int SimulationIntervalMilliseconds = 200;
    private const double SimulationStepSeconds = SimulationIntervalMilliseconds / 1000.0;
    private const double MaximumCatchUpHours = 24 * 365;
    private const double EmptyEpsilonLitres = 0.000001;

    private readonly Dictionary<BlockPos, IHydraulicNetworkNode> loadedNodes = new();
    private ICoreServerAPI? sapi;
    private long tickListenerId;
    private int simulationTick;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        tickListenerId = api.Event.RegisterGameTickListener(
            OnServerTick, SimulationIntervalMilliseconds, SimulationIntervalMilliseconds / 2);
    }

    public void Register(IHydraulicNetworkNode node) => loadedNodes[node.Position.Copy()] = node;

    public void Unregister(IHydraulicNetworkNode node) => loadedNodes.Remove(node.Position);

    public override void Dispose()
    {
        if (sapi != null && tickListenerId != 0) sapi.Event.UnregisterGameTickListener(tickListenerId);
        loadedNodes.Clear();
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
        if (sapi == null || loadedNodes.Count == 0) return;
        bool runConsumers = ++simulationTick % 5 == 0;
        HashSet<IHydraulicNetworkNode> visited = new();
        foreach (IHydraulicNetworkNode seed in loadedNodes.Values.ToArray())
        {
            if (visited.Contains(seed)) continue;
            NetworkComponent component = DiscoverComponent(seed, visited);
            if (!component.Complete)
            {
                SetFault(component.Nodes, "waiting-for-chunks");
                continue;
            }
            Simulate(component, runConsumers);
        }
    }

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

        if (pipes.Any(pipe => pipe.ContentAmountLitres > EmptyEpsilonLitres &&
                              pipe.CurrentContentCode == null))
        {
            SetFault(component.Nodes, "invalid-content");
            return;
        }

        Dictionary<BlockPos, int> pipeByPosition = pipes.Select((pipe, index) => (pipe, index))
            .ToDictionary(pair => pair.pipe.Pos.Copy(), pair => pair.index);
        List<AssetLocation> storedCodes = pipes
            .Where(pipe => pipe.HasContent && pipe.CurrentContentCode != null)
            .Select(pipe => pipe.CurrentContentCode!)
            .Distinct(AssetLocationComparer.Instance)
            .ToList();
        if (storedCodes.Count > 1)
        {
            SetFault(component.Nodes, "mixed-content");
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
                SetFault(component.Nodes, "mixed-sources");
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
            SetFault(component.Nodes, "invalid-content");
            return;
        }

        PipeContentPhase phase = PipeContent.Phase(sapi.World, contentCode);
        int count = pipes.Count;
        double[] amounts = new double[count];
        double[] temperatures = new double[count];
        double[] energies = new double[count];
        double[] previousPressures = new double[count];
        double[] stepPressures = new double[count];
        double[] generatedPressures = new double[count];
        double[] throughputs = new double[count];
        double[][] nozzleFlows = Enumerable.Range(0, count)
            .Select(_ => new double[BlockFacing.NumberOfFaces]).ToArray();
        double[] strongestFlow = new double[count];
        BlockFacing?[] flowDirections = new BlockFacing?[count];
        double[] lastConsumerHours = pipes.Select(pipe => pipe.LastSimulationTotalHours).ToArray();
        bool[] liquidOverflowOutlets = pipes
            .Select(pipe => phase == PipeContentPhase.Liquid && HasUpwardAirOutlet(pipe))
            .ToArray();
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
            previousPressures[i] = pipe.CurrentContentCode?.Equals(contentCode) == true
                ? pipe.CurrentPressure
                : phase == PipeContentPhase.Gas
                    ? -HydraulicMath.AmbientPressureKPa
                    : HydraulicMath.LiquidBasePressure(0);
        }

        foreach (BlockEntityCreativeFluidPump source in creativeSources)
        {
            PumpOffer offer = source.GetOffer();
            if (offer.Pressure <= 0) continue;
            if (!offer.ContentCode.Equals(contentCode))
            {
                boundaryMismatch = true;
                continue;
            }
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!source.CanConnect(face) ||
                    !pipeByPosition.TryGetValue(source.Pos.AddCopy(face), out int pipeIndex) ||
                    !pipes[pipeIndex].CanConnect(face.Opposite)) continue;
                generatedPressures[pipeIndex] = Math.Max(generatedPressures[pipeIndex], offer.Pressure);
            }
        }

        if (phase == PipeContentPhase.Liquid)
        {
            for (int i = 0; i < count; i++)
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockEntityFluidPipe pipe = pipes[i];
                if (pipe.GetAddon(face) != HydraulicFaceAddon.PipeNozzle) continue;
                NozzleContainer? container = GetNozzleContainer(pipe, face);
                if (container == null) continue;
                ItemStack? stack = container.Value.Interface.GetContent(container.Value.Position);
                if (stack?.Collectible.Code.Equals(contentCode) != true) continue;
                double fill = container.Value.Interface.GetCurrentLitres(container.Value.Position) /
                    Math.Max(0.001, container.Value.Interface.CapacityLitres);
                double pressure = HydraulicMath.LiquidContainerPressure(
                    fill, container.Value.Position.Y, NozzleWorldY(pipe, face));
                generatedPressures[i] = Math.Max(generatedPressures[i], pressure);
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (phase == PipeContentPhase.Gas)
            {
                stepPressures[i] = HydraulicMath.GasGaugePressure(amounts[i], temperatures[i]);
                continue;
            }

            double strongest = generatedPressures[i];
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!pipes[i].IsConnected(face) ||
                    !pipeByPosition.TryGetValue(pipes[i].Pos.AddCopy(face), out int neighbor)) continue;
                strongest = Math.Max(strongest, HydraulicMath.PropagatedLiquidPressure(
                    previousPressures[neighbor], pipes[neighbor].Pos.Y, pipes[i].Pos.Y,
                    amounts[neighbor] / HydraulicMath.PipeCapacityLitres));
            }
            stepPressures[i] = HydraulicMath.LiquidPressureFromPrevious(
                amounts[i] / HydraulicMath.PipeCapacityLitres, strongest);
        }

        List<FlowIntent> intents = PlanPipeFlows(
            pipes, pipeByPosition, amounts, stepPressures, phase);
        double[] receiverCapacities = liquidOverflowOutlets
            .Select(hasOverflow => hasOverflow
                ? double.PositiveInfinity
                : phase == PipeContentPhase.Liquid
                    ? HydraulicMath.PipeCapacityLitres
                    : double.PositiveInfinity)
            .ToArray();
        ApplyPipeFlows(intents, amounts, energies, temperatures, throughputs,
            strongestFlow, flowDirections, receiverCapacities);

        foreach (BlockEntityCreativeFluidPump source in creativeSources)
        {
            PumpOffer offer = source.GetOffer();
            if (offer.Pressure <= 0 || !offer.ContentCode.Equals(contentCode)) continue;
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!source.CanConnect(face) ||
                    !pipeByPosition.TryGetValue(source.Pos.AddCopy(face), out int pipeIndex) ||
                    !pipes[pipeIndex].CanConnect(face.Opposite)) continue;
                double receivingPressure = liquidOverflowOutlets[pipeIndex] &&
                    amounts[pipeIndex] >= HydraulicMath.PipeCapacityLitres
                    ? 0
                    : stepPressures[pipeIndex];
                double wanted = HydraulicMath.RequestedTransferLitres(
                    offer.Pressure - receivingPressure, SimulationStepSeconds, phase);
                if (phase == PipeContentPhase.Liquid)
                {
                    if (!liquidOverflowOutlets[pipeIndex])
                    {
                        wanted = Math.Min(wanted, HydraulicMath.PipeCapacityLitres - amounts[pipeIndex]);
                    }
                }
                else
                {
                    double targetAmount = HydraulicMath.GasStandardLitresForGaugePressure(
                        offer.Pressure, source.ConfiguredTemperatureC);
                    wanted = Math.Min(wanted, Math.Max(0, targetAmount - amounts[pipeIndex]));
                }
                if (wanted <= EmptyEpsilonLitres) continue;
                AddAmount(pipeIndex, wanted, source.ConfiguredTemperatureC, amounts, energies, temperatures);
                RecordFlow(pipeIndex, wanted / SimulationStepSeconds, face,
                    throughputs, strongestFlow, flowDirections);
            }
        }

        for (int i = 0; i < count; i++)
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            BlockEntityFluidPipe pipe = pipes[i];
            bool atmosphericOutlet = IsAtmosphericOutlet(pipe, face);
            if (face == BlockFacing.UP && atmosphericOutlet) continue;
            double signedFlow;
            if (pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle)
            {
                signedFlow = ProcessNozzle(
                    pipe, face, contentCode, phase, stepPressures[i], i,
                    amounts, energies, temperatures, ref boundaryMismatch, ref gasContainerBlocked);
            }
            else if (atmosphericOutlet)
            {
                signedFlow = ProcessAirOutlet(
                    face, phase, stepPressures[i], i, amounts, energies, temperatures);
            }
            else continue;
            nozzleFlows[i][face.Index] = signedFlow;
            if (Math.Abs(signedFlow) > EmptyEpsilonLitres)
            {
                RecordFlow(i, Math.Abs(signedFlow),
                    signedFlow > 0 ? face : face.Opposite,
                    throughputs, strongestFlow, flowDirections);
            }
        }

        for (int i = 0; i < count; i++)
        {
            BlockEntityFluidPipe pipe = pipes[i];
            if (!IsAtmosphericOutlet(pipe, BlockFacing.UP)) continue;
            double signedFlow = ProcessAirOutlet(
                BlockFacing.UP, phase, stepPressures[i], i,
                amounts, energies, temperatures);
            nozzleFlows[i][BlockFacing.UP.Index] = signedFlow;
            if (Math.Abs(signedFlow) > EmptyEpsilonLitres)
            {
                RecordFlow(i, Math.Abs(signedFlow), BlockFacing.UP,
                    throughputs, strongestFlow, flowDirections);
            }
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
        bool networkEmpty = totalAmount <= EmptyEpsilonLitres;
        string status = networkEmpty
            ? "empty"
            : gasContainerBlocked
                ? "gas-container-blocked"
                : boundaryMismatch
                    ? "content-mismatch"
                    : "running";

        for (int i = 0; i < count; i++)
        {
            if (amounts[i] <= EmptyEpsilonLitres)
            {
                amounts[i] = 0;
                temperatures[i] = PipeContent.DefaultTemperatureC(contentCode);
            }
            pipes[i].ApplySimulationState(
                networkEmpty ? null : contentCode,
                amounts[i], temperatures[i], networkEmpty ? 0 : stepPressures[i], status,
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

    private List<FlowIntent> PlanPipeFlows(
        IReadOnlyList<BlockEntityFluidPipe> pipes,
        IReadOnlyDictionary<BlockPos, int> pipeByPosition,
        IReadOnlyList<double> amounts,
        IReadOnlyList<double> pressures,
        PipeContentPhase phase)
    {
        List<FlowIntent> intents = new();
        for (int i = 0; i < pipes.Count; i++)
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (!pipes[i].IsConnected(face) ||
                !pipeByPosition.TryGetValue(pipes[i].Pos.AddCopy(face), out int other) || other <= i) continue;
            double ownPotential = HydraulicMath.HydraulicPotential(pressures[i], pipes[i].Pos.Y, phase);
            double otherPotential = HydraulicMath.HydraulicPotential(pressures[other], pipes[other].Pos.Y, phase);
            int from = ownPotential >= otherPotential ? i : other;
            int to = from == i ? other : i;
            BlockFacing direction = from == i ? face : face.Opposite;
            if (amounts[from] <= EmptyEpsilonLitres) continue;
            double wanted = HydraulicMath.RequestedTransferLitres(
                Math.Abs(ownPotential - otherPotential), SimulationStepSeconds, phase);
            if (wanted > EmptyEpsilonLitres) intents.Add(new FlowIntent(from, to, wanted, direction));
        }
        return intents;
    }

    private static void ApplyPipeFlows(
        IReadOnlyList<FlowIntent> intents,
        double[] amounts,
        double[] energies,
        double[] temperatures,
        double[] throughputs,
        double[] strongestFlow,
        BlockFacing?[] flowDirections,
        IReadOnlyList<double> receiverCapacities)
    {
        PipeTransferIntent[] solverIntents = intents
            .Select(intent => new PipeTransferIntent(intent.From, intent.To, intent.Litres)).ToArray();
        double[] actualTransfers = PipeFlowSolver.ScaleTransfers(
            amounts, receiverCapacities, solverIntents, HydraulicMath.MaximumTransferFractionPerStep);

        double[] deltas = new double[amounts.Length];
        double[] energyDeltas = new double[amounts.Length];
        for (int intentIndex = 0; intentIndex < intents.Count; intentIndex++)
        {
            FlowIntent intent = intents[intentIndex];
            double actual = actualTransfers[intentIndex];
            if (actual <= EmptyEpsilonLitres) continue;
            double temperature = temperatures[intent.From];
            deltas[intent.From] -= actual;
            deltas[intent.To] += actual;
            energyDeltas[intent.From] -= actual * temperature;
            energyDeltas[intent.To] += actual * temperature;
            double rate = actual / SimulationStepSeconds;
            RecordFlow(intent.From, rate, intent.DirectionFrom,
                throughputs, strongestFlow, flowDirections);
            RecordFlow(intent.To, rate, intent.DirectionFrom,
                throughputs, strongestFlow, flowDirections);
        }

        for (int i = 0; i < amounts.Length; i++)
        {
            amounts[i] = Math.Max(0, amounts[i] + deltas[i]);
            energies[i] = Math.Max(0, energies[i] + energyDeltas[i]);
            temperatures[i] = amounts[i] > EmptyEpsilonLitres
                ? energies[i] / amounts[i]
                : temperatures[i];
        }
    }

    private double ProcessNozzle(
        BlockEntityFluidPipe pipe,
        BlockFacing face,
        AssetLocation contentCode,
        PipeContentPhase phase,
        double pipePressure,
        int pipeIndex,
        double[] amounts,
        double[] energies,
        double[] temperatures,
        ref bool boundaryMismatch,
        ref bool gasContainerBlocked)
    {
        BlockPos targetPos = pipe.Pos.AddCopy(face);
        Block targetBlock = sapi!.World.BlockAccessor.GetBlock(targetPos);
        if (targetBlock.BlockMaterial == EnumBlockMaterial.Air)
        {
            return ProcessAirOutlet(
                face, phase, pipePressure, pipeIndex, amounts, energies, temperatures);
        }

        NozzleContainer? found = GetNozzleContainer(pipe, face);
        if (found == null) return 0;
        if (phase == PipeContentPhase.Gas)
        {
            gasContainerBlocked = true;
            return 0;
        }

        NozzleContainer container = found.Value;
        ItemStack? containerContent = container.Interface.GetContent(container.Position);
        double currentLitres = container.Interface.GetCurrentLitres(container.Position);
        double fill = currentLitres / Math.Max(0.001, container.Interface.CapacityLitres);
        double containerPressure = HydraulicMath.LiquidContainerPressure(
            fill, container.Position.Y, NozzleWorldY(pipe, face));

        if (containerContent != null && containerContent.StackSize > 0 &&
            !containerContent.Collectible.Code.Equals(contentCode))
        {
            boundaryMismatch = true;
            return 0;
        }

        if (container.Source != null && containerContent != null &&
            containerPressure > (HasUpwardAirOutlet(pipe) &&
                amounts[pipeIndex] >= HydraulicMath.PipeCapacityLitres ? 0 : pipePressure) +
                HydraulicMath.FlowDeadbandKPa)
        {
            WaterTightContainableProps? props = container.Interface.GetContentProps(container.Position);
            if (props == null || props.ItemsPerLitre <= 0) return 0;
            double receivingPressure = HasUpwardAirOutlet(pipe) &&
                amounts[pipeIndex] >= HydraulicMath.PipeCapacityLitres ? 0 : pipePressure;
            double wanted = HydraulicMath.RequestedTransferLitres(
                containerPressure - receivingPressure, SimulationStepSeconds, PipeContentPhase.Liquid);
            bool canOverflow = HasUpwardAirOutlet(pipe);
            if (!canOverflow)
            {
                wanted = Math.Min(wanted,
                    Math.Max(0, HydraulicMath.PipeCapacityLitres - amounts[pipeIndex]));
            }
            double exactItems = wanted * props.ItemsPerLitre + pipe.GetNozzleItemRemainder(face);
            int maximumItems = canOverflow
                ? (int)Math.Ceiling(wanted * props.ItemsPerLitre)
                : (int)Math.Floor(
                    Math.Max(0, HydraulicMath.PipeCapacityLitres - amounts[pipeIndex]) * props.ItemsPerLitre);
            int requestedItems = Math.Min((int)Math.Floor(exactItems), maximumItems);
            pipe.SetNozzleItemRemainder(face, exactItems - requestedItems);
            if (requestedItems <= 0) return 0;
            ItemStack? taken = container.Source.TryTakeContent(container.Position, requestedItems);
            int actualItems = taken?.StackSize ?? 0;
            if (actualItems <= 0) return 0;
            double actual = canOverflow
                ? actualItems / props.ItemsPerLitre
                : Math.Min(HydraulicMath.PipeCapacityLitres - amounts[pipeIndex],
                    actualItems / props.ItemsPerLitre);
            double temperature = taken!.Collectible.GetTemperature(sapi.World, taken);
            AddAmount(pipeIndex, actual, temperature, amounts, energies, temperatures);
            return -actual / SimulationStepSeconds;
        }

        if (container.Sink == null || amounts[pipeIndex] <= EmptyEpsilonLitres ||
            pipePressure <= containerPressure + HydraulicMath.FlowDeadbandKPa) return 0;
        Item? item = sapi.World.GetItem(contentCode);
        WaterTightContainableProps? contentProps = item?.Attributes?["waterTightContainerProps"]
            .AsObject<WaterTightContainableProps>(null, contentCode.Domain);
        if (item == null || contentProps == null || contentProps.ItemsPerLitre <= 0) return 0;
        double desired = HydraulicMath.RequestedTransferLitres(
            pipePressure - containerPressure, SimulationStepSeconds, PipeContentPhase.Liquid);
        desired = Math.Min(desired, amounts[pipeIndex]);
        if (desired <= EmptyEpsilonLitres) return 0;
        ItemStack offered = new(item, Math.Max(1, (int)Math.Ceiling(desired * contentProps.ItemsPerLitre)));
        offered.Collectible.SetTemperature(sapi.World, offered, (float)temperatures[pipeIndex], false);
        int movedItems = container.Sink.TryPutLiquid(container.Position, offered, (float)desired);
        double moved = Math.Min(amounts[pipeIndex], movedItems / contentProps.ItemsPerLitre);
        if (moved <= EmptyEpsilonLitres) return 0;
        RemoveAmount(pipeIndex, moved, amounts, energies, temperatures);
        return moved / SimulationStepSeconds;
    }

    private AssetLocation? GetNozzleSourceCode(BlockEntityFluidPipe pipe, BlockFacing face)
    {
        NozzleContainer? found = GetNozzleContainer(pipe, face);
        if (found?.Source == null) return null;
        ItemStack? content = found.Value.Interface.GetContent(found.Value.Position);
        return content is { StackSize: > 0 } &&
            PipeContent.IsVintageStoryLiquid(sapi!.World, content.Collectible.Code)
            ? content.Collectible.Code
            : null;
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

    private double ProcessAirOutlet(
        BlockFacing face,
        PipeContentPhase phase,
        double pipePressure,
        int pipeIndex,
        double[] amounts,
        double[] energies,
        double[] temperatures)
    {
        double ventable = phase == PipeContentPhase.Gas
            ? HydraulicMath.GasVentableStandardLitres(
                amounts[pipeIndex], temperatures[pipeIndex])
            : face == BlockFacing.UP
                ? HydraulicMath.LiquidOverflowLitres(amounts[pipeIndex])
                : amounts[pipeIndex];
        if (ventable <= EmptyEpsilonLitres) return 0;
        double removed;
        if (phase == PipeContentPhase.Liquid && face == BlockFacing.UP)
        {
            removed = ventable;
        }
        else
        {
            double outletPressure = phase == PipeContentPhase.Gas
                ? HydraulicMath.GasGaugePressure(amounts[pipeIndex], temperatures[pipeIndex])
                : pipePressure;
            double rate = HydraulicMath.NozzleInventoryRateLitresPerSecond +
                Math.Max(0, outletPressure) * 0.25;
            removed = Math.Min(ventable, rate * SimulationStepSeconds);
        }
        RemoveAmount(pipeIndex, removed, amounts, energies, temperatures);
        return removed / SimulationStepSeconds;
    }

    private bool HasUpwardAirOutlet(BlockEntityFluidPipe pipe) =>
        IsAtmosphericOutlet(pipe, BlockFacing.UP);

    private bool IsAtmosphericOutlet(BlockEntityFluidPipe pipe, BlockFacing face)
    {
        if (pipe is BlockEntityIrrigatorPipe) return false;
        if (sapi!.World.BlockAccessor.GetBlock(pipe.Pos.AddCopy(face)).BlockMaterial !=
            EnumBlockMaterial.Air) return false;
        return pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle || pipe.IsPortEnabled(face);
    }

    private static double NozzleWorldY(BlockEntityFluidPipe pipe, BlockFacing face) =>
        pipe.Pos.Y + 0.5 + face.Normali.Y * 0.5;

    private static void AddAmount(
        int index,
        double litres,
        double temperature,
        double[] amounts,
        double[] energies,
        double[] temperatures)
    {
        if (litres <= 0) return;
        amounts[index] += litres;
        energies[index] += litres * temperature;
        temperatures[index] = energies[index] / amounts[index];
    }

    private static void RemoveAmount(
        int index,
        double litres,
        double[] amounts,
        double[] energies,
        double[] temperatures)
    {
        double removed = Math.Min(amounts[index], Math.Max(0, litres));
        amounts[index] -= removed;
        energies[index] = Math.Max(0, energies[index] - removed * temperatures[index]);
        if (amounts[index] <= EmptyEpsilonLitres)
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

    private static void SetFault(IEnumerable<IHydraulicNetworkNode> nodes, string status)
    {
        double[] noNozzleFlow = new double[BlockFacing.NumberOfFaces];
        foreach (IHydraulicNetworkNode node in nodes)
        {
            if (node is BlockEntityFluidPipe pipe)
            {
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

    private readonly record struct FlowIntent(int From, int To, double Litres, BlockFacing DirectionFrom);
    private readonly record struct NetworkComponent(List<IHydraulicNetworkNode> Nodes, bool Complete);
    private readonly record struct NozzleContainer(
        BlockPos Position,
        ILiquidInterface Interface,
        ILiquidSource? Source,
        ILiquidSink? Sink);

    private sealed class AssetLocationComparer : IEqualityComparer<AssetLocation>
    {
        public static readonly AssetLocationComparer Instance = new();
        public bool Equals(AssetLocation? x, AssetLocation? y) => x?.Equals(y) == true;
        public int GetHashCode(AssetLocation obj) => obj.GetHashCode();
    }
}
