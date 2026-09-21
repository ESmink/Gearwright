using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Air;

/// <summary>Generator-independent delivery to one adjacent consumer, with vanilla compatibility.</summary>
public static class AirDelivery
{
    /// <summary>
    /// Send this simulation step's airflow through the source's outlet.
    /// False means no loaded receiver; the air vents without being queued.
    /// </summary>
    public static bool Deliver(ICoreAPI api, BlockPos source, AirFlow flow)
    {
        if (api.Side != EnumAppSide.Server || !flow.IsValid) return false;
        var accessor = api.World.BlockAccessor;
        BlockPos target = source.AddCopy(flow.Direction);
        if (accessor.GetChunkAtBlockPos(source) == null || accessor.GetChunkAtBlockPos(target) == null) return false;
        Block block = accessor.GetBlock(target);
        // Native lookup supports the block, its entity, and both behavior lists.
        IAirReceiver? receiver = block.GetInterface<IAirReceiver>(api.World, target);
        if (receiver != null)
        {
            receiver.ReceiveAir(api.World, target, flow);
            return true;
        }
        IBellowsAirReceiver? vanilla = block.GetInterface<IBellowsAirReceiver>(api.World, target);
        if (vanilla == null) return false;
        vanilla.BlowAirInto(api.World, target, (float)flow.Amount, flow.Direction);
        return true;
    }
}
