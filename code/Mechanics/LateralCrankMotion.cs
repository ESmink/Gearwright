using System;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Projects one physical journal orbit into the shaft or pump's local frame.</summary>
internal static class LateralCrankMotion
{
    public static double AngleInFrame(
        double mechanicalAngle,
        BlockFacing rotationAxis,
        BlockFacing localX,
        BlockFacing localY)
    {
        if (rotationAxis.Axis == EnumAxis.Y || localX.Axis != rotationAxis.Axis ||
            localX.Axis == localY.Axis)
            throw new ArgumentException("A journal needs a horizontal axle and a perpendicular slider.");

        double angle = double.IsFinite(mechanicalAngle) ? mechanicalAngle : 0;
        Vec3i axis = rotationAxis.Normali;
        // Rodrigues' rotation of world UP around the same signed axis used by
        // the vanilla mechanical renderer. AngleRad already handles network reversal.
        double sin = Math.Sin(angle);
        double x = -axis.Z * sin;
        double y = Math.Cos(angle);
        double z = axis.X * sin;
        Vec3i u = localX.Normali;
        Vec3i v = localY.Normali;
        double alongY = x * v.X + y * v.Y + z * v.Z;
        double alongZ = x * (u.Y * v.Z - u.Z * v.Y) +
                        y * (u.Z * v.X - u.X * v.Z) +
                        z * (u.X * v.Y - u.Y * v.X);
        return Math.Atan2(alongZ, alongY);
    }
}
