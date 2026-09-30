using System;
using System.Linq;
using Gearwright.Hydraulics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

internal sealed class PneumaticPlacementPlan
{
    internal required BlockSelection Selection;
    internal required ItemSlot Slot;
    internal required BlockFacing Input;
    internal required BlockFacing Output;
    internal BlockFacing Inventory = BlockFacing.DOWN;
    internal long Expires;
    internal bool Released;
}

internal static class PneumaticPlacement
{
    internal static BlockFacing Aim(double x, double y, double z, EnumAxis? excluded = null) =>
        BlockFacing.ALLFACES.Where(f => f.Axis != excluded)
            .OrderByDescending(f => x * f.Normali.X + y * f.Normali.Y + z * f.Normali.Z).First();

    internal static BlockFacing Aim(IPlayer player, EnumAxis? excluded = null)
    {
        var view = player.Entity.Pos.GetViewVector();
        return Aim(view.X, view.Y, view.Z, excluded);
    }

    internal static BlockFacing InventoryDirection(PneumaticPlacementPlan plan, IPlayer player)
    {
        // Both stages use look direction: normal faces back along the view,
        // Shift faces along it. A hovered inventory never overrides that choice.
        var direction = Aim(player, plan.Output.Axis);
        return player.Entity.Controls.ShiftKey ? direction : direction.Opposite;
    }

    internal static BlockFacing DefaultInventory(BlockFacing output) => output.IsHorizontal ? BlockFacing.DOWN : BlockFacing.NORTH;

    internal static float[] EndpointMatrix(BlockFacing output, BlockFacing inventory) =>
        PumpOrientation.Matrix(output, (inventory.Axis == output.Axis ? DefaultInventory(output) : inventory).Opposite);

    internal static BlockSelection Target(IWorldAccessor world, Block block, BlockSelection selected)
    {
        var pos = selected.Position.Copy();
        bool offset = !world.BlockAccessor.GetBlock(pos).IsReplacableBy(block);
        if (offset) pos.Add(selected.Face);
        return new BlockSelection { Position = pos, Face = selected.Face, HitPosition = selected.HitPosition, DidOffset = offset };
    }

    internal static bool InReach(IPlayer player, BlockPos pos) =>
        player.Entity.Pos.Dimension == pos.dimension &&
        player.Entity.Pos.XYZ.SquareDistanceTo(pos.ToVec3d().Add(.5, .5, .5)) <= 64;
}
