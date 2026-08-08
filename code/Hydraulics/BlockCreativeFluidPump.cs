using Vintagestory.API.Common;

namespace Gearwright.Hydraulics;

public sealed class BlockCreativeFluidPump : BlockHydraulicPump
{
    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityCreativeFluidPump pump)
        {
            return false;
        }
        if (world.Side == EnumAppSide.Client) pump.OpenConfigurationDialog();
        return true;
    }
}
