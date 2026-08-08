using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public sealed class BlockPassiveFluidPump : BlockHydraulicPump
{
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityPassiveFluidPump pump) return;
        if (neibpos.Equals(pos.UpCopy()) || neibpos.Equals(pos.AddCopy(pump.Facing.Opposite)))
        {
            pump.OnTankNeighbourChanged();
        }
    }

    public override bool DoPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ItemStack byItemStack)
    {
        BlockFacing facing = SuggestedHVOrientation(byPlayer, blockSel)[0];
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack)) return false;

        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPassiveFluidPump pump)
        {
            pump.SetFacing(facing);
        }
        return true;
    }
}
