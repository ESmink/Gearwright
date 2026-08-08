using System;

namespace Gearwright.Hydraulics;

/// <summary>Stable hydraulic tuning shared by simulation and contract tests.</summary>
public static class HydraulicMath
{
    public const double FullSprinklerPressure = 100;
    public const double MaximumCreativePressure = 1000;
    public const double FullSprinklerLitresPerDay = 8;
    public const double HorizontalTankReserveFraction = 0.25;
    public const double HorizontalPumpMaximumPressure = 75;

    public static double ConsumerPressure(double networkPressure, int consumers)
    {
        if (consumers <= 0 || !double.IsFinite(networkPressure)) return 0;
        return Math.Max(0, networkPressure) / consumers;
    }

    public static double Performance(double consumerPressure) =>
        Math.Clamp(consumerPressure / FullSprinklerPressure, 0, 1);

    public static int Reach(double consumerPressure)
    {
        if (consumerPressure < 1) return 0;
        if (consumerPressure < 25) return 1;
        if (consumerPressure < 50) return 2;
        if (consumerPressure < 75) return 3;
        return 4;
    }

    public static double LitresPerDayPerConsumer(double consumerPressure) =>
        FullSprinklerLitresPerDay * Performance(consumerPressure);

    public static double PassivePressure(double fillFraction, bool horizontal)
    {
        double fill = Math.Clamp(fillFraction, 0, 1);
        if (!horizontal) return FullSprinklerPressure * Math.Sqrt(fill);
        if (fill <= HorizontalTankReserveFraction) return 0;

        double available = (fill - HorizontalTankReserveFraction) /
            (1 - HorizontalTankReserveFraction);
        return HorizontalPumpMaximumPressure * Math.Sqrt(available);
    }

    public static bool IsInsideCircularReach(int deltaX, int deltaZ, int reach) =>
        reach > 0 && deltaX * deltaX + deltaZ * deltaZ <= reach * reach;
}
