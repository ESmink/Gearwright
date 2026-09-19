using Gearwright.Mechanics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Places an orientable pressure vessel, with or without its drive shaft.</summary>
public sealed class BlockReciprocatingPump : Block
{
    public override void OnNeighbourBlockChange(
        IWorldAccessor world,
        BlockPos pos,
        BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        world.BlockAccessor.MarkBlockDirty(pos);
    }

    public override bool DoPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ItemStack byItemStack)
    {
        bool hasDrive = TryFindCrank(
            world, blockSel.Position,
            out BlockFacing driveFace,
            out BlockFacing shaftPositive,
            out _);
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack))
        {
            return false;
        }

        if (!hasDrive)
        {
            driveFace = blockSel.Face;
            shaftPositive = DefaultOutputFace(byPlayer, driveFace);
        }
        BlockFacing outputFace = byPlayer.Entity.Controls.ShiftKey
            ? shaftPositive.Opposite
            : shaftPositive;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityReciprocatingPump pump)
        {
            pump.ConfigurePlacement(driveFace, outputFace);
        }
        return true;
    }

    private static BlockFacing DefaultOutputFace(IPlayer byPlayer, BlockFacing driveFace)
    {
        BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
        if (facing.Axis != driveFace.Axis) return facing;
        return driveFace.Axis == EnumAxis.X ? BlockFacing.SOUTH : BlockFacing.EAST;
    }

    private static bool TryFindCrank(
        IWorldAccessor world,
        BlockPos pumpPosition,
        out BlockFacing driveFace,
        out BlockFacing shaftPositive,
        out BEBehaviorMPLateralCrank? crank)
    {
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            BlockPos crankPosition = pumpPosition.AddCopy(face);
            crank = world.BlockAccessor.GetBlockEntity(crankPosition)?
                .GetBehavior<BEBehaviorMPLateralCrank>();
            if (crank == null) continue;
            BlockFacing[] axisFaces = crank.AxisFaces();
            if (face.Axis == axisFaces[0].Axis) continue;
            driveFace = face;
            shaftPositive = axisFaces[1];
            return true;
        }
        driveFace = BlockFacing.UP;
        shaftPositive = BlockFacing.EAST;
        crank = null;
        return false;
    }
}
