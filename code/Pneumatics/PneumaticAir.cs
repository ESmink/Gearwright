using System;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Initial simulation tuning, in transport air units, not litres or kPa.</summary>
internal static class PneumaticAir
{
    public const double UnitsPerBellowsUnit = 100;
    public const double LegacyPlenumCapacity = 40;
    public const double PlenumCapacity = 2400;
    // One full stroke supplies about 20 units. Three strokes per second
    // balance this quadratic outlet at half reserve (60 units/second).
    public const double OutletUnitsPerSecond = 240;
    public const double TubeLossPerSecond = .1;
    public const double EndpointLossPerSecond = .5;
    public const double MaximumStepSeconds = .25;
    public const int MaximumNodes = 1024;

    public static double OutletRate(double storedAir)
    {
        if (!double.IsFinite(storedAir) || storedAir <= 0) return 0;
        double fill = Math.Clamp(storedAir / PlenumCapacity, 0, 1);
        return OutletUnitsPerSecond * fill * fill;
    }

    public static bool ValidStep(double seconds) =>
        double.IsFinite(seconds) && seconds > 0 && seconds <= MaximumStepSeconds;

    public static bool ValidFace(BlockFacing? face) =>
        face != null && Array.IndexOf(BlockFacing.ALLFACES, face) >= 0;
}
