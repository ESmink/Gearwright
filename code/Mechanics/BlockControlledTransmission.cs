using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>A hard boundary between two Vanilla mechanical networks.</summary>
public class BlockControlledTransmission : BlockTransmission
{
    public override bool HasMechPowerConnectorAt(
        IWorldAccessor world,
        BlockPos pos,
        BlockFacing face,
        BlockMPBase block) => false;

    public override MechanicalNetwork GetNetwork(IWorldAccessor world, BlockPos pos) => null!;

    public override void DidConnectAt(
        IWorldAccessor world,
        BlockPos pos,
        BlockFacing face)
    {
        NotifyBoundary(world, pos);
    }

    public override void OnNeighbourBlockChange(
        IWorldAccessor world,
        BlockPos pos,
        BlockPos neibpos)
    {
        NotifyBoundary(world, pos);
    }

    private static void NotifyBoundary(IWorldAccessor world, BlockPos pos)
    {
        world.BlockAccessor.GetBlockEntity(pos)?
            .GetBehavior<BEBehaviorMPControlledTransmission>()?
            .RefreshNow();
    }
}
