using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Maps approved local X=output, Y=drive geometry to a block orientation.</summary>
internal static class PumpOrientation
{
    public static Matrixf ApplyConnectingRodPose(Matrixf matrix, ReciprocatingPumpVisualPose pose) =>
        matrix.Translate(0, pose.RodOffsetY, pose.RodOffsetZ)
            .Translate(.5f, 1.5f, .5f)
            .RotateX(pose.RodAngleRadians)
            .Translate(-.5f, -1.5f, -.5f);

    public static float[] Matrix(BlockFacing output, BlockFacing drive)
    {
        if (output.Axis == drive.Axis)
            throw new ArgumentException("Pump output and drive faces must be perpendicular.");

        Vec3i x = output.Normali;
        Vec3i y = drive.Normali;
        Vec3i z = new(
            x.Y * y.Z - x.Z * y.Y,
            x.Z * y.X - x.X * y.Z,
            x.X * y.Y - x.Y * y.X);
        return new[]
        {
            (float)x.X, (float)x.Y, (float)x.Z, 0,
            (float)y.X, (float)y.Y, (float)y.Z, 0,
            (float)z.X, (float)z.Y, (float)z.Z, 0,
            .5f - .5f * (x.X + y.X + z.X),
            .5f - .5f * (x.Y + y.Y + z.Y),
            .5f - .5f * (x.Z + y.Z + z.Z),
            1
        };
    }
}
