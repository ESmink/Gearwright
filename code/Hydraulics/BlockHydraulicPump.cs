using Vintagestory.API.Common;

namespace Gearwright.Hydraulics;

public class BlockHydraulicPump : Block
{
    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        if (world.Side == EnumAppSide.Server &&
            world.Api.ModLoader.GetModSystem<HydraulicNetworkSystem>()
                .WouldPlacementJoinDifferentContents(blockSel.Position))
        {
            failureCode = "gearwright-pipe-content-conflict";
            return false;
        }
        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }
}
