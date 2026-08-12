using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Per-part collision and selection bounds for the 3x3 flywheel.</summary>
public static class FlywheelBounds
{
    private static readonly Cuboidf[] NorthSouth =
    {
        // Laminated foot, two sloping supports, and the bearing block.
        new(-1.00f, -1.00f, 0.05f, 2.00f, -0.58f, 0.95f),
        new(-0.82f, -0.78f, 0.07f, 0.34f, 0.58f, 0.93f),
        new( 0.66f, -0.78f, 0.07f, 1.82f, 0.58f, 0.93f),
        new( 0.24f,  0.23f, 0.03f, 0.76f, 0.80f, 0.97f),

        // The rotating stone ring's swept envelope. Keeping the four sides
        // separate leaves the open centre selectable without making the
        // complete 3x3 footprint a solid wall.
        new(-0.60f,  1.02f, 0.02f, 1.60f, 1.80f, 0.98f),
        new(-0.60f, -0.98f, 0.02f, 1.60f, -0.04f, 0.98f),
        new(-0.98f, -0.58f, 0.02f, -0.18f, 1.58f, 0.98f),
        new( 1.18f, -0.58f, 0.02f,  1.98f, 1.58f, 0.98f),
    };

    /// <param name="alongX">True for the west/east shaft variant.</param>
    /// <param name="controllerOffset">
    /// Vector from the queried multiblock part to the controller block.
    /// </param>
    public static Cuboidf[] ForPart(bool alongX, Vec3i controllerOffset)
    {
        List<Cuboidf> result = new();
        foreach (Cuboidf source in NorthSouth)
        {
            Cuboidf oriented = alongX
                ? new Cuboidf(
                    source.Z1, source.Y1, 1 - source.X2,
                    source.Z2, source.Y2, 1 - source.X1)
                : source;

            float x1 = Math.Max(0, oriented.X1 + controllerOffset.X);
            float y1 = Math.Max(0, oriented.Y1 + controllerOffset.Y);
            float z1 = Math.Max(0, oriented.Z1 + controllerOffset.Z);
            float x2 = Math.Min(1, oriented.X2 + controllerOffset.X);
            float y2 = Math.Min(1, oriented.Y2 + controllerOffset.Y);
            float z2 = Math.Min(1, oriented.Z2 + controllerOffset.Z);
            if (x2 > x1 && y2 > y1 && z2 > z1)
            {
                result.Add(new Cuboidf(x1, y1, z1, x2, y2, z2));
            }
        }
        return result.ToArray();
    }
}
