using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

public sealed partial class HydraulicNetworkSystem
{
    private bool SolvePressureFlow(List<BlockEntityFluidPipe> pipes,
        IReadOnlyDictionary<BlockPos, int> indexes, List<ReciprocatingPumpPort> ports,
        List<BlockEntityCreativeFluidPump> sources, AssetLocation content, PipeContentPhase phase,
        double[] amounts, double[] temperatures, double[] energies, double[] pressures,
        double[] throughputs, double[] strongestFlow, BlockFacing?[] directions, double[][] nozzleFlows,
        ref bool boundaryMismatch, ref bool gasContainerBlocked)
    {
        double conductance = phase == PipeContentPhase.Gas
            ? HydraulicMath.GasConductanceStandardLitresPerSecondPerKPa
            : HydraulicMath.PipeConductanceLitresPerSecondPerKPa;
        List<FluidCell> cells = new();
        List<FluidLink> links = new();
        List<BlockFacing> linkDirections = new();
        List<FluidBoundary> boundaries = new();
        List<BlockFacing> boundaryFaces = new();
        List<bool> nozzleBoundaries = new();
        Dictionary<int, (NozzleContainer Container, WaterTightContainableProps Props)> containerBoundaries = new();
        List<(ReciprocatingPumpPort Port, int Link)> pumpLinks = new();
        double[] suction = new double[pipes.Count];
        var portsByPipe = ports.ToLookup(port => port.PipeIndex);
        for (int i = 0; i < pipes.Count; i++)
        {
            if (phase == PipeContentPhase.Liquid)
            {
                foreach (BlockFacing face in BlockFacing.ALLFACES)
                    if (pipes[i].CanConnect(face) && indexes.TryGetValue(pipes[i].Pos.AddCopy(face), out int neighbor) &&
                        pipes[neighbor].CanConnect(face.Opposite))
                        suction[i] = Math.Min(suction[i], HydraulicMath.PropagatedLiquidSuction(
                            pipes[neighbor].DrivenSuctionKPa, pipes[neighbor].Pos.Y, pipes[i].Pos.Y));
                foreach (var port in portsByPipe[i])
                    if (port.Pump.CanWriteState && port.PumpFace == port.Pump.InputFace)
                        suction[i] = Math.Min(suction[i], port.Pump.BoundaryPressureKPa(port.PumpFace));
            }
            cells.Add(new(amounts[i], temperatures[i], HydraulicMath.PipeCapacityLitres, pipes[i].Pos.Y, phase, Suction: suction[i]));
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (pipes[i].CanConnect(face) && indexes.TryGetValue(pipes[i].Pos.AddCopy(face), out int other) &&
                    other > i && pipes[other].CanConnect(face.Opposite))
                {
                    links.Add(new(i, other, conductance));
                    linkDirections.Add(face);
                }
                if (IsAtmosphericOutlet(pipes[i], face))
                {
                    // Upper openings discharge only after liquid reaches their
                    // brim; side/down openings drain, gases vent above ambient.
                    double target = phase == PipeContentPhase.Gas ? 0 : face == BlockFacing.UP
                        ? HydraulicMath.LiquidFullnessPressureKPa
                        : -HydraulicMath.NozzleInventoryRateLitresPerSecond / HydraulicMath.AirOutletConductancePerSecondPerKPa;
                    boundaries.Add(new(i, target, HydraulicMath.AirOutletConductancePerSecondPerKPa, false));
                    boundaryFaces.Add(face);
                    nozzleBoundaries.Add(true);
                }
                if (pipes[i].GetAddon(face) == HydraulicFaceAddon.PipeNozzle &&
                    GetNaturalLiquidSource(pipes[i], face) is { } natural &&
                    phase == PipeContentPhase.Liquid && natural.ContentCode.Equals(content))
                {
                    boundaries.Add(new(i, -ReciprocatingPumpMath.InfiniteLiquidSourceSuctionKPa,
                        conductance, true, natural.TemperatureC));
                    boundaryFaces.Add(face);
                    nozzleBoundaries.Add(true);
                }
                if (pipes[i].GetAddon(face) != HydraulicFaceAddon.PipeNozzle) continue;
                if (GetNaturalLiquidSource(pipes[i], face) is { } sourceLiquid)
                    boundaryMismatch |= phase != PipeContentPhase.Liquid || !sourceLiquid.ContentCode.Equals(content);
                if (GetNozzleContainer(pipes[i], face) is not { } container) continue;
                if (phase == PipeContentPhase.Gas) { gasContainerBlocked = true; continue; }
                ItemStack? stack = container.Interface.GetContent(container.Position);
                if (stack is { StackSize: > 0 } && !stack.Collectible.Code.Equals(content))
                { boundaryMismatch = true; continue; }
                double litres = container.Interface.GetCurrentLitres(container.Position);
                double targetPressure = HydraulicMath.LiquidContainerPressure(
                    litres / Math.Max(.001, container.Interface.CapacityLitres), container.Position.Y, NozzleWorldY(pipes[i], face));
                var props = sapi!.World.GetItem(content)?.Attributes?["waterTightContainerProps"]
                    .AsObject<WaterTightContainableProps>(null, content.Domain);
                if (props == null || props.ItemsPerLitre <= 0) continue;
                void AddContainerBoundary(bool source, double maximum, double temperature)
                {
                    containerBoundaries[boundaries.Count] = (container, props);
                    boundaries.Add(new(i, targetPressure, conductance, source, temperature, maximum));
                    boundaryFaces.Add(face); nozzleBoundaries.Add(true);
                }
                if (container.Source != null && stack is { StackSize: > 0 })
                    AddContainerBoundary(true, litres, stack.Collectible.GetTemperature(sapi.World, stack));
                if (container.Sink != null)
                    AddContainerBoundary(false, Math.Max(0, container.Interface.CapacityLitres - litres), temperatures[i]);
            }
        }
        foreach (var source in sources)
        {
            PumpOffer offer = source.GetOffer();
            if (offer.Pressure <= 0) continue;
            if (!offer.ContentCode.Equals(content)) { boundaryMismatch = true; continue; }
            foreach (BlockFacing face in BlockFacing.ALLFACES)
                if (source.CanConnect(face) && indexes.TryGetValue(source.Pos.AddCopy(face), out int i) &&
                    pipes[i].CanConnect(face.Opposite))
                {
                    boundaries.Add(new(i, offer.Pressure, conductance, true, source.ConfiguredTemperatureC));
                    boundaryFaces.Add(face.Opposite);
                    nozzleBoundaries.Add(false);
                }
        }
        int pipeLinkCount = links.Count;
        foreach (var port in ports)
        {
            var pump = port.Pump;
            if (!pump.CanWriteState || (pump.CurrentContentCode != null && !pump.CurrentContentCode.Equals(content))) continue;
            bool intake = port.PumpFace == pump.InputFace && pump.CurrentStroke == ReciprocatingPumpStroke.Suction;
            bool output = port.PumpFace == pump.OutputFace && pump.CurrentStroke == ReciprocatingPumpStroke.Pressure;
            if (!intake && !output) continue;
            int pumpIndex = cells.Count;
            cells.Add(new(pump.ContentAmountLitres, pump.ContentTemperatureC,
                pump.ChamberVolumeLitres, pump.Pos.Y, phase, Pump: true));
            double room = intake && phase == PipeContentPhase.Liquid
                ? Math.Max(0, pump.ChamberVolumeLitres - pump.ContentAmountLitres) : double.PositiveInfinity;
            pumpLinks.Add((port, links.Count));
            links.Add(intake ? new(port.PipeIndex, pumpIndex, conductance, true, room)
                : new(pumpIndex, port.PipeIndex, ReciprocatingPumpMath.DischargeConductance(phase), true));
        }
        if (!PipePressureSolver.TryStep(cells, links, boundaries, SimulationStepSeconds, out var solved)) return false;

        for (int i = 0; i < pipes.Count; i++)
        {
            amounts[i] = solved.Amounts[i];
            energies[i] = amounts[i] * solved.Temperatures[i];
        }
        // Remove every provisional container exchange before calling inventory
        // APIs. Only their actual whole-item debits/credits enter stored litres.
        foreach (var entry in containerBoundaries)
        {
            int k = entry.Key, i = boundaries[k].Node;
            double flux = solved.BoundaryLitres[k];
            amounts[i] += flux;
            energies[i] += flux * (flux >= 0 ? cells[i].Temperature : boundaries[k].Temperature);
        }
        foreach (var entry in containerBoundaries)
        {
            int k = entry.Key, i = boundaries[k].Node;
            double planned = solved.BoundaryLitres[k];
            if (planned == 0) continue;
            var (container, props) = entry.Value;
            double exactItems = Math.Abs(planned) * props.ItemsPerLitre + pipes[i].GetNozzleItemRemainder(boundaryFaces[k]);
            int requested = (int)Math.Min(int.MaxValue, Math.Floor(exactItems));
            if (planned > 0) requested = Math.Min(requested, (int)Math.Floor(Math.Max(0, amounts[i]) * props.ItemsPerLitre));
            pipes[i].SetNozzleItemRemainder(boundaryFaces[k], exactItems - requested);
            double actual = 0, temperature = cells[i].Temperature;
            if (requested > 0 && planned < 0)
            {
                ItemStack? taken = container.Source!.TryTakeContent(container.Position, requested);
                actual = -(double)(taken?.StackSize ?? 0) / props.ItemsPerLitre;
                if (taken != null) temperature = taken.Collectible.GetTemperature(sapi!.World, taken);
            }
            else if (requested > 0)
            {
                ItemStack offered = new(sapi!.World.GetItem(content), requested);
                temperature = amounts[i] > 0 ? energies[i] / amounts[i] : cells[i].Temperature;
                offered.Collectible.SetTemperature(sapi.World, offered, (float)temperature, false);
                actual = container.Sink!.TryPutLiquid(container.Position, offered, requested / props.ItemsPerLitre) / (double)props.ItemsPerLitre;
            }
            amounts[i] -= actual;
            energies[i] -= actual * temperature;
            solved.BoundaryLitres[k] = actual;
        }

        // All receiver identities, content types, read-only states and
        // chunk availability were checked before this pure solve. Commit only
        // its actual check-valve flux; pump and pipes share the same debit/credit.
        foreach (var pair in pumpLinks)
        {
            double flux = solved.LinkLitres[pair.Link];
            if (flux <= 0) continue;
            var pump = pair.Port.Pump;
            int pipeIndex = pair.Port.PipeIndex;
            if (pair.Port.PumpFace == pump.InputFace)
                pump.Receive(content, flux, cells[pipeIndex].Temperature);
            else pump.Provide(content, flux);
            RecordFlow(pipeIndex, flux / SimulationStepSeconds,
                pair.Port.PumpFace == pump.InputFace ? pair.Port.PumpFace.Opposite : pair.Port.PumpFace,
                throughputs, strongestFlow, directions);
        }
        for (int k = 0; k < pipeLinkCount; k++)
        {
            double flux = solved.LinkLitres[k];
            BlockFacing face = flux >= 0 ? linkDirections[k] : linkDirections[k].Opposite;
            double rate = Math.Abs(flux) / SimulationStepSeconds;
            RecordFlow(links[k].From, rate, face, throughputs, strongestFlow, directions);
            RecordFlow(links[k].To, rate, face, throughputs, strongestFlow, directions);
        }
        for (int k = 0; k < boundaries.Count; k++)
        {
            double rate = solved.BoundaryLitres[k] / SimulationStepSeconds;
            int i = boundaries[k].Node;
            if (nozzleBoundaries[k]) nozzleFlows[i][boundaryFaces[k].Index] += rate;
            RecordFlow(i, Math.Abs(rate), rate >= 0 ? boundaryFaces[k] : boundaryFaces[k].Opposite,
                throughputs, strongestFlow, directions);
        }
        for (int i = 0; i < pipes.Count; i++)
        {
            temperatures[i] = amounts[i] > 0 ? energies[i] / amounts[i] : cells[i].Temperature;
            pressures[i] = HydraulicMath.StoredPressure(amounts[i], temperatures[i], phase, suction[i]);
            pipes[i].DrivenSuctionKPa = suction[i];
        }
        foreach (var port in ports)
            port.Pump.SetTransferPort(port.PumpFace, pipes[port.PipeIndex], pressures[port.PipeIndex]);
        return true;
    }
}
