using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>A horizontal inline axle whose controller occupies the E4 wheel centre.</summary>
public sealed class BlockSmallFlywheel : BlockAxle, IMultiBlockColSelBoxes
{
    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        FlywheelBounds.ForPart(AlongX, new Vec3i());

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        FlywheelBounds.ForPart(AlongX, new Vec3i());

    public Cuboidf[] MBGetSelectionBoxes(
        IBlockAccessor blockAccessor,
        BlockPos pos,
        Vec3i offset) => FlywheelBounds.ForPart(AlongX, offset);

    public Cuboidf[] MBGetCollisionBoxes(
        IBlockAccessor blockAccessor,
        BlockPos pos,
        Vec3i offset) => FlywheelBounds.ForPart(AlongX, offset);

    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        BlockSelection placement = blockSel;
        string? connectedRotation = FindConnectedRotation(world, placement.Position);
        if (connectedRotation == null && blockSel.Face == BlockFacing.UP)
        {
            placement = blockSel.Clone();
            placement.Position = blockSel.Position.UpCopy();
            connectedRotation = FindConnectedRotation(world, placement.Position);
        }

        string rotation = connectedRotation ??
            (BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Axis == EnumAxis.X ? "we" : "ns");
        Block? selected = world.GetBlock(CodeWithVariant("rotation", rotation));
        if (selected is not BlockSmallFlywheel flywheel ||
            !flywheel.CanPlaceBlock(world, byPlayer, placement, ref failureCode) ||
            !flywheel.DoPlaceBlock(world, byPlayer, placement, itemstack))
        {
            return false;
        }

        bool connected = false;
        foreach (BlockFacing face in FacesFor(rotation))
        {
            BlockPos neighbourPos = placement.Position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is not IMechanicalPowerBlock neighbour ||
                !neighbour.HasMechPowerConnectorAt(
                    world, neighbourPos, face.Opposite, flywheel))
            {
                continue;
            }

            neighbour.DidConnectAt(world, neighbourPos, face.Opposite);
            flywheel.WasPlaced(world, placement.Position, face);
            connected = true;
        }

        if (!connected) flywheel.WasPlaced(world, placement.Position, null);
        return true;
    }

    private string? FindConnectedRotation(IWorldAccessor world, BlockPos position)
    {
        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            BlockPos neighbourPos = position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is IMechanicalPowerBlock neighbour &&
                neighbour.HasMechPowerConnectorAt(world, neighbourPos, face.Opposite, this))
            {
                return face.Axis == EnumAxis.X ? "we" : "ns";
            }
        }
        return null;
    }

    private static BlockFacing[] FacesFor(string rotation) => rotation == "we"
        ? new[] { BlockFacing.WEST, BlockFacing.EAST }
        : new[] { BlockFacing.NORTH, BlockFacing.SOUTH };

    private bool AlongX => Variant["rotation"] == "we";
}
