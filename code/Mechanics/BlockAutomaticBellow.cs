using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Mechanics;

/// <summary>Own item identity, using the existing bellows body and entity hooks.</summary>
public sealed class BlockAutomaticBellow : BlockBellows
{
    internal AssetLocation OrientedCode(BlockFacing face) =>
        new(Code.Domain, "automatic-bellow-" + face.Code);

    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer,
        ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        // Native HorizontalOrientable truncates a dashed base code. Resolve the
        // complete Automatic Bellow code explicitly for every nozzle direction.
        BlockFacing side = SuggestedHVOrientation(byPlayer, blockSel)[0];
        Block? selected = world.GetBlock(OrientedCode(side));
        return selected is BlockAutomaticBellow &&
            selected.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) &&
            selected.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    public override AssetLocation GetRotatedBlockCode(int angle)
    {
        BlockFacing facing = BlockFacing.FromCode(Variant["side"]);
        int index = GameMath.Mod(facing.HorizontalAngleIndex - angle / 90, 4);
        return OrientedCode(BlockFacing.HORIZONTALS_ANGLEORDER[index]);
    }
}
