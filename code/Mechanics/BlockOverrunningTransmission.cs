using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>A directional automatic boundary between two Vanilla mechanical networks.</summary>
public sealed class BlockOverrunningTransmission : BlockMPBase
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

    private static void NotifyBoundary(IWorldAccessor world, BlockPos pos)
    {
        world.BlockAccessor.GetBlockEntity(pos)?
            .GetBehavior<BEBehaviorMPOverrunningTransmission>()?
            .RefreshNow();
    }
}
