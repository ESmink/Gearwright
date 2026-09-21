using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Air;

/// <summary>Software port implemented by a block, entity or one of their behaviors.</summary>
public interface IAirReceiver
{
    /// <summary>
    /// Apply this step's airflow on the server. Multiple generators add their
    /// amounts; a delivery does not imply continuing flow in later steps.
    /// </summary>
    void ReceiveAir(IWorldAccessor world, BlockPos position, AirFlow flow);
}
