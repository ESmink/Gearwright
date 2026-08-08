using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Server-authoritative, bufferless pressure and liquid settlement.</summary>
public sealed class HydraulicNetworkSystem : ModSystem
{
    private const double MaximumCatchUpHours = 24 * 365;
    private const double CatchUpStepHours = 24;

    private readonly Dictionary<BlockPos, IHydraulicNetworkNode> loadedNodes = new();
    private ICoreServerAPI? sapi;
    private long tickListenerId;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        tickListenerId = api.Event.RegisterGameTickListener(OnServerTick, 1000, 250);
    }

    public void Register(IHydraulicNetworkNode node)
    {
        loadedNodes[node.Position.Copy()] = node;
    }

    public void Unregister(IHydraulicNetworkNode node)
    {
        loadedNodes.Remove(node.Position);
    }

    public override void Dispose()
    {
        if (sapi != null && tickListenerId != 0) sapi.Event.UnregisterGameTickListener(tickListenerId);
        loadedNodes.Clear();
        sapi = null;
    }

    private void OnServerTick(float _)
    {
        if (sapi == null || loadedNodes.Count == 0) return;
        HashSet<IHydraulicNetworkNode> visited = new();
        foreach (IHydraulicNetworkNode seed in loadedNodes.Values.ToArray())
        {
            if (visited.Contains(seed)) continue;
            NetworkComponent component = DiscoverComponent(seed, visited);
            if (!component.Complete)
            {
                SetState(component.Nodes, null, 0, "waiting-for-chunks");
                continue;
            }
            Settle(component);
        }
    }

    private NetworkComponent DiscoverComponent(IHydraulicNetworkNode seed, HashSet<IHydraulicNetworkNode> visited)
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

    private void Settle(NetworkComponent component)
    {
        List<BlockEntityHydraulicPump> pumps = component.Nodes.OfType<BlockEntityHydraulicPump>().ToList();
        List<BlockEntityFluidPipe> sprinklers = component.Nodes.OfType<BlockEntityFluidPipe>()
            .Where(pipe => pipe.HasSprinkler).ToList();
        double now = sapi!.World.Calendar.TotalHours;

        if (pumps.Count == 0)
        {
            SetState(component.Nodes, null, 0, "no-pump");
            return;
        }
        if (sprinklers.Count == 0)
        {
            foreach (BlockEntityHydraulicPump pump in pumps) pump.SetLastSettledTotalHours(now);
            SetState(component.Nodes, null, 0, "stopped-no-consumers");
            return;
        }

        List<PumpOffer> currentOffers = PositiveOffers(pumps);
        if (!TryGetLiquid(currentOffers, out AssetLocation? currentLiquid))
        {
            foreach (BlockEntityHydraulicPump pump in pumps) pump.SetLastSettledTotalHours(now);
            SetState(component.Nodes, null, 0, "mixed-liquid");
            return;
        }
        if (currentLiquid == null || currentOffers.Count == 0)
        {
            foreach (BlockEntityHydraulicPump pump in pumps) pump.SetLastSettledTotalHours(now);
            SetState(component.Nodes, null, 0, "no-pressure");
            return;
        }

        double start = pumps.Where(pump => pump.LastSettledTotalHours >= 0)
            .Select(pump => pump.LastSettledTotalHours).DefaultIfEmpty(now).Max();
        double elapsed = Math.Clamp(now - start, 0, MaximumCatchUpHours);
        double weightedPressureHours = 0;
        double settledHours = 0;
        AssetLocation settledLiquid = currentLiquid;

        while (elapsed > 0.000001)
        {
            double stepHours = Math.Min(CatchUpStepHours, elapsed);
            List<PumpOffer> offers = PositiveOffers(pumps);
            if (!TryGetLiquid(offers, out AssetLocation? liquid) || liquid == null || offers.Count == 0)
            {
                settledHours += stepHours;
                elapsed -= stepHours;
                continue;
            }

            settledLiquid = liquid;
            double offeredNetworkPressure = offers.Sum(offer => offer.Pressure);
            double offeredConsumerPressure = HydraulicMath.ConsumerPressure(offeredNetworkPressure, sprinklers.Count);
            double requestedLitres = HydraulicMath.LitresPerDayPerConsumer(offeredConsumerPressure) *
                sprinklers.Count * stepHours / 24;
            double effectiveNetworkPressure = 0;

            foreach (PumpOffer offer in offers)
            {
                double share = offeredNetworkPressure <= 0 ? 0 : offer.Pressure / offeredNetworkPressure;
                double requestedFromPump = requestedLitres * share;
                double delivered = offer.Pump.ConsumeLitres(requestedFromPump);
                double deliveryFraction = requestedFromPump <= 0 ? 1 : Math.Clamp(delivered / requestedFromPump, 0, 1);
                effectiveNetworkPressure += offer.Pressure * deliveryFraction;
            }

            double effectiveConsumerPressure = HydraulicMath.ConsumerPressure(effectiveNetworkPressure, sprinklers.Count);
            weightedPressureHours += effectiveConsumerPressure * stepHours;
            settledHours += stepHours;
            elapsed -= stepHours;
        }

        foreach (BlockEntityHydraulicPump pump in pumps) pump.SetLastSettledTotalHours(now);

        double averagePressure = settledHours > 0
            ? weightedPressureHours / settledHours
            : HydraulicMath.ConsumerPressure(currentOffers.Sum(offer => offer.Pressure), sprinklers.Count);
        foreach (BlockEntityFluidPipe sprinkler in sprinklers)
        {
            ApplySprinkler(sprinkler, settledLiquid, averagePressure);
        }

        List<PumpOffer> afterOffers = PositiveOffers(pumps);
        if (!TryGetLiquid(afterOffers, out AssetLocation? afterLiquid) || afterLiquid == null)
        {
            SetState(component.Nodes, null, 0, afterOffers.Count == 0 ? "no-pressure" : "mixed-liquid");
            return;
        }
        double pressure = HydraulicMath.ConsumerPressure(afterOffers.Sum(offer => offer.Pressure), sprinklers.Count);
        SetState(
            component.Nodes,
            afterLiquid,
            pressure,
            pressure > 0 ? "running" : "no-pressure",
            afterOffers.Select(offer => offer.Pump));
    }

    private static List<PumpOffer> PositiveOffers(IEnumerable<BlockEntityHydraulicPump> pumps) =>
        pumps.Select(pump => pump.GetOffer()).Where(offer => offer.Pressure > 0).ToList();

    private static bool TryGetLiquid(IEnumerable<PumpOffer> offers, out AssetLocation? liquid)
    {
        liquid = null;
        foreach (PumpOffer offer in offers)
        {
            if (liquid == null) liquid = offer.LiquidCode;
            else if (!liquid.Equals(offer.LiquidCode)) return false;
        }
        return true;
    }

    private static void SetState(
        IEnumerable<IHydraulicNetworkNode> nodes,
        AssetLocation? liquid,
        double pressure,
        string status,
        IEnumerable<BlockEntityHydraulicPump>? activePumps = null)
    {
        List<IHydraulicNetworkNode> nodeList = nodes.ToList();
        Dictionary<IHydraulicNetworkNode, BlockFacing?> directions = pressure > 0 && activePumps != null
            ? CalculateFlowDirections(nodeList, activePumps)
            : new Dictionary<IHydraulicNetworkNode, BlockFacing?>();

        foreach (IHydraulicNetworkNode node in nodeList)
        {
            directions.TryGetValue(node, out BlockFacing? direction);
            node.SetNetworkState(liquid, pressure, status, direction);
        }
    }

    private static Dictionary<IHydraulicNetworkNode, BlockFacing?> CalculateFlowDirections(
        IReadOnlyCollection<IHydraulicNetworkNode> nodes,
        IEnumerable<BlockEntityHydraulicPump> activePumps)
    {
        Dictionary<BlockPos, IHydraulicNetworkNode> byPosition = nodes.ToDictionary(
            node => node.Position.Copy(), node => node);
        Dictionary<IHydraulicNetworkNode, BlockFacing?> directions = new();
        Queue<IHydraulicNetworkNode> pending = new();

        foreach (BlockEntityHydraulicPump pump in activePumps
                     .Distinct()
                     .OrderBy(pump => pump.Position.X)
                     .ThenBy(pump => pump.Position.Y)
                     .ThenBy(pump => pump.Position.Z))
        {
            if (!directions.TryAdd(pump, null)) continue;
            pending.Enqueue(pump);
        }

        while (pending.Count > 0)
        {
            IHydraulicNetworkNode current = pending.Dequeue();
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (!current.CanConnect(face) ||
                    !byPosition.TryGetValue(current.Position.AddCopy(face), out IHydraulicNetworkNode? next) ||
                    !next.CanConnect(face.Opposite) || directions.ContainsKey(next)) continue;

                // The BFS walks away from a supplying pump, so this is the direction
                // in which liquid enters and crosses the newly reached node.
                directions[next] = face;
                pending.Enqueue(next);
            }
        }

        return directions;
    }

    private void ApplySprinkler(BlockEntityFluidPipe sprinkler, AssetLocation liquid, double pressure)
    {
        int reach = HydraulicMath.Reach(pressure);
        if (reach <= 0) return;
        bool freshWater = liquid.Equals(new AssetLocation(HydraulicCodes.FreshWater));
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
            else
            {
                MarkCropExposure(farmland, liquid);
            }
        }
    }

    private void MarkCropExposure(BlockEntityFarmland farmland, AssetLocation liquid)
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

        ITreeAttribute exposure = existing?.Clone() ?? new Vintagestory.API.Datastructures.TreeAttribute();
        exposure.SetInt("schemaVersion", 1);
        exposure.SetString("liquidCode", liquid.ToString());
        exposure.SetDouble("exposedAtTotalHours", sapi.World.Calendar.TotalHours);
        farmland.CropAttributes[HydraulicCodes.FarmlandExposureTree] = exposure;
        farmland.MarkDirty(false);
    }

    private readonly record struct NetworkComponent(List<IHydraulicNetworkNode> Nodes, bool Complete);
}
