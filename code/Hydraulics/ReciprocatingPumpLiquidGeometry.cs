using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Liquid bounds inside the approved chamber, in block units.</summary>
internal static class ReciprocatingPumpLiquidGeometry
{
    public const float Bottom = 5.65f / 16f;
    public const float UpperPistonBottom = 12.22f / 16f;
    public const float MinX = 3.85f / 16f;
    public const float MaxX = 12.15f / 16f;
    public const float MinZ = 4.251f / 16f;
    public const float MaxZ = 1 - MinZ;
    public const float PistonInset = .0001f;

    public static float SurfaceHeight(double amountLitres, float pistonBottom, double chamberVolumeLitres)
    {
        double volume = Math.Max(ReciprocatingPumpMath.ClearanceVolumeLitres, chamberVolumeLitres);
        float fraction = (float)Math.Clamp(double.IsFinite(amountLitres) ? amountLitres / volume : 0, 0, 1);
        // Map the stored fraction into the actual space under the live piston.
        // The approved visual clearance is larger than the simulation's dead
        // volume; scaling against the full stroke leaves a gap on short strokes.
        float level = Bottom + (pistonBottom - Bottom) * fraction;
        return Math.Max(Bottom, Math.Min(pistonBottom - PistonInset, level));
    }

    // ScrollingLiquidSurface is a north-facing XY quad. Rotate it into XZ,
    // with its normal pointing towards the piston, and lift to the liquid level.
    public static Matrixf ApplyTopPose(Matrixf matrix, float height) =>
        matrix.Translate(0, height, 0).RotateX(GameMath.PIHALF);
}
