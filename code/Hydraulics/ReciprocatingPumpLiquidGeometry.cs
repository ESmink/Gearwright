using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Liquid bounds inside the approved chamber, in block units.</summary>
internal static class ReciprocatingPumpLiquidGeometry
{
    public const float Bottom = 6.145f / 16f;
    public const float UpperPistonBottom = 12.22f / 16f;
    public const float MinX = 3.85f / 16f;
    public const float MaxX = 12.15f / 16f;
    public const float MinZ = 4.251f / 16f;
    public const float MaxZ = 1 - MinZ;
    public const float PistonInset = .0001f;
    public const float StrokeHeight = 6f / 16f;

    public static float SurfaceHeight(double amountLitres, float pistonBottom, double chamberVolumeLitres)
    {
        // The approved floor leaves exactly the simulation's clearance volume.
        // Stored litres therefore have one height throughout free piston travel;
        // only contact/compression clamps the surface to the piston underside.
        double amount = double.IsFinite(amountLitres) ? Math.Max(0, amountLitres) : 0;
        float level = Bottom + StrokeHeight * (float)(amount / ReciprocatingPumpMath.StrokeCapacityLitres);
        return Math.Max(Bottom, Math.Min(pistonBottom - PistonInset, level));
    }

    // ScrollingLiquidSurface is a north-facing XY quad. Rotate it into XZ,
    // with its normal pointing towards the piston, and lift to the liquid level.
    public static Matrixf ApplyTopPose(Matrixf matrix, float height) =>
        matrix.Translate(0, height, 0).RotateX(GameMath.PIHALF);
}
