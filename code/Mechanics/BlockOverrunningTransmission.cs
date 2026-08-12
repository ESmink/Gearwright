using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>A directional automatic boundary between two Vanilla mechanical networks.</summary>
public sealed class BlockOverrunningTransmission : BlockControlledTransmission
{
    public BlockFacing InputFace
    {
        get
        {
            bool positive = Variant["input"] == "positive";
            return Variant["orientation"] == "we"
                ? (positive ? BlockFacing.EAST : BlockFacing.WEST)
                : (positive ? BlockFacing.SOUTH : BlockFacing.NORTH);
        }
    }

    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        BlockFacing preferred = blockSel.Face.IsHorizontal
            ? blockSel.Face.Opposite
            : BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Opposite;
        BlockFacing input = FindSingleConnectedFace(world, blockSel.Position) ?? preferred;
        string orientation = input.Axis == EnumAxis.X ? "we" : "ns";
        string inputSign = input == BlockFacing.EAST || input == BlockFacing.SOUTH
            ? "positive"
            : "negative";

        Block? oriented = world.GetBlock(CodeWithVariant("orientation", orientation));
        Block? selected = oriented == null
            ? null
            : world.GetBlock(oriented.CodeWithVariant("input", inputSign));
        if (selected is not BlockOverrunningTransmission transmission ||
            !transmission.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) ||
            !transmission.DoPlaceBlock(world, byPlayer, blockSel, itemstack))
        {
            return false;
        }

        foreach (BlockFacing face in new[] { transmission.InputFace, transmission.InputFace.Opposite })
        {
            BlockPos neighbourPos = blockSel.Position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is not IMechanicalPowerBlock neighbour ||
                !neighbour.HasMechPowerConnectorAt(
                    world, neighbourPos, face.Opposite, transmission))
            {
                continue;
            }
            neighbour.DidConnectAt(world, neighbourPos, face.Opposite);
        }
        return true;
    }

    private BlockFacing? FindSingleConnectedFace(IWorldAccessor world, BlockPos position)
    {
        BlockFacing? found = null;
        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            BlockPos neighbourPos = position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is not IMechanicalPowerBlock neighbour ||
                !neighbour.HasMechPowerConnectorAt(world, neighbourPos, face.Opposite, this))
            {
                continue;
            }
            if (found != null) return null;
            found = face;
        }
        return found;
    }
}
