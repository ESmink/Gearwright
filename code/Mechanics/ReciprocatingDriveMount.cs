using System;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Derived attachment geometry; no seat assignment is written to a save.</summary>
public static class ReciprocatingDriveMount
{
    public const float BearingResistance = .0015f;
    public const float SeatPitch = 1.4f / 16;
    public const float MaximumOffset = 2.1f / 16;

    public static float CenteredOffset(int index, int count)
    {
        if (count is < 1 or > 4 || index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return (index - (count - 1) * .5f) * SeatPitch;
    }

    // The ordering is canonical for a physical shaft axis. Reversing the
    // pump's plumbing or the power connection cannot reorder its neighbors.
    internal static BlockFacing[] Faces(EnumAxis axis) => axis == EnumAxis.X
        ? new[] { BlockFacing.DOWN, BlockFacing.UP, BlockFacing.NORTH, BlockFacing.SOUTH }
        : new[] { BlockFacing.DOWN, BlockFacing.UP, BlockFacing.EAST, BlockFacing.WEST };
}
