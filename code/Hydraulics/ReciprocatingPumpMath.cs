using System;

namespace Gearwright.Hydraulics;

public enum ReciprocatingPumpStroke
{
    Stationary = 0,
    Suction = 1,
    Pressure = 2
}

public readonly record struct ReciprocatingPumpVisualPose(
    float PistonOffsetY,
    float RodOffsetY,
    float RodOffsetZ,
    float RodAngleRadians,
    float WetIntakeOffsetY,
    float WetOutputOffsetY,
    float BreatherIntakeOffsetY,
    float BreatherExhaustOffsetY);

/// <summary>Pure chamber, valve, source-water, and mechanical-load policy.</summary>
public static class ReciprocatingPumpMath
{
    private const double ModelUnitsPerBlock = 16;
    private const double VisualCrankRadius = 3;
    private const double VisualRodLength = 6;
    private const double WetCheckTravel = .36;
    private const double AirCheckTravel = .22;
    public const double StrokeCapacityLitres = 4;
    public const double ClearanceVolumeLitres = 0.05;
    public const double LiquidCompressionStiffnessKPa = 150000;
    public const double ServicePressureKPa = 1500;
    // Numerical guards, far above the working range; neither is a hard stop.
    public const double MaximumChamberPressureKPa = 1000000000;
    public const double InfiniteLiquidSourceSuctionKPa = 70;
    public const double FutureRapidWaterEnergyBudgetJoulesPerLitre = 60;
    public const float BaseMechanicalResistance = 0.0015f;
    public const double MechanicalResistancePerJoulePerRadian = 0.00025;
    public const float MaximumMechanicalResistance = 1000000f;
    public const double MotionEpsilon = 0.000001;

    // Intake follows the faster pipe supply. Keep exhaust and its predicted
    // mechanical load at the established valve conductance.
    internal static double DischargeConductance(PipeContentPhase phase) =>
        phase == PipeContentPhase.Gas ? 0.4 : 2;

    internal static double RequestedDischargeLitres(double pressureDifference, double seconds, PipeContentPhase phase) =>
        Math.Max(0, (double.IsFinite(pressureDifference) ? pressureDifference : 0) - HydraulicMath.FlowDeadbandKPa) *
        Math.Max(0, double.IsFinite(seconds) ? seconds : 0) * DischargeConductance(phase);

    public static double PistonVolumeFraction(double crankAngleRadians)
    {
        double angle = double.IsFinite(crankAngleRadians) ? crankAngleRadians : 0;
        double crosshead = VisualCrankRadius * Math.Cos(angle) - RodReach(angle);
        return Math.Clamp((crosshead + VisualRodLength + VisualCrankRadius) /
            (2 * VisualCrankRadius), 0, 1);
    }

    public static double ChamberVolumeLitres(double crankAngleRadians) =>
        ClearanceVolumeLitres + StrokeCapacityLitres * PistonVolumeFraction(crankAngleRadians);

    public static ReciprocatingPumpVisualPose VisualPose(
        double visualAngleRadians,
        ReciprocatingPumpStroke stroke)
    {
        double angle = double.IsFinite(visualAngleRadians) ? visualAngleRadians : 0;
        double pinY = VisualCrankRadius * Math.Cos(angle);
        double pinZ = VisualCrankRadius * Math.Sin(angle);
        double crossheadY = pinY - RodReach(angle);
        double middleY = (pinY + crossheadY) * .5;
        double middleZ = pinZ * .5;
        double rodAngle = Math.Atan2(pinZ, pinY - crossheadY);
        double travel = Math.Abs(Math.Sin(angle));
        double pressure = stroke == ReciprocatingPumpStroke.Pressure ? travel : 0;
        double suction = stroke == ReciprocatingPumpStroke.Suction ? travel : 0;

        // Phase zero has crossheadY=-3 and rod midpoint at the crank center.
        return new ReciprocatingPumpVisualPose(
            (float)((crossheadY + 3) / ModelUnitsPerBlock),
            (float)(middleY / ModelUnitsPerBlock),
            (float)(middleZ / ModelUnitsPerBlock),
            (float)rodAngle,
            (float)(WetCheckTravel * suction / ModelUnitsPerBlock),
            (float)(-WetCheckTravel * pressure / ModelUnitsPerBlock),
            (float)(-AirCheckTravel * pressure / ModelUnitsPerBlock),
            (float)(AirCheckTravel * suction / ModelUnitsPerBlock));
    }

    private static double RodReach(double angle)
    {
        double pinZ = VisualCrankRadius * Math.Sin(angle);
        return Math.Sqrt(VisualRodLength * VisualRodLength - pinZ * pinZ);
    }

    public static ReciprocatingPumpStroke Stroke(double previousVolumeLitres, double volumeLitres)
    {
        double previous = double.IsFinite(previousVolumeLitres) ? previousVolumeLitres : volumeLitres;
        double current = double.IsFinite(volumeLitres) ? volumeLitres : previous;
        if (current > previous + MotionEpsilon) return ReciprocatingPumpStroke.Suction;
        if (current < previous - MotionEpsilon) return ReciprocatingPumpStroke.Pressure;
        return ReciprocatingPumpStroke.Stationary;
    }

    public static double DrivingBoundaryPressureKPa(
        double chamberPressureKPa, ReciprocatingPumpStroke stroke, bool input)
    {
        if (!double.IsFinite(chamberPressureKPa)) return 0;
        if (input && stroke == ReciprocatingPumpStroke.Suction) return Math.Min(0, chamberPressureKPa);
        if (!input && stroke == ReciprocatingPumpStroke.Pressure) return Math.Max(0, chamberPressureKPa);
        return 0;
    }

    public static double ChamberPressureKPa(
        double amountLitres,
        double temperatureC,
        PipeContentPhase phase,
        double volumeLitres)
    {
        double amount = Math.Max(0, double.IsFinite(amountLitres) ? amountLitres : 0);
        double volume = Math.Max(ClearanceVolumeLitres,
            double.IsFinite(volumeLitres) ? volumeLitres : ClearanceVolumeLitres);
        if (phase == PipeContentPhase.Gas)
        {
            return Math.Clamp(
                HydraulicMath.GasGaugePressureForVolume(amount, temperatureC, volume),
                -HydraulicMath.AmbientPressureKPa,
                MaximumChamberPressureKPa);
        }

        double ratio = amount / volume;
        if (ratio <= 1)
        {
            return -HydraulicMath.AmbientPressureKPa * (1 - ratio);
        }
        return Math.Min(MaximumChamberPressureKPa,
            (ratio - 1) * LiquidCompressionStiffnessKPa);
    }

    /// <summary>Amount that can leave without dropping below the outlet pressure.</summary>
    public static double DischargeToEquilibriumLitres(
        double amountLitres, double temperatureC, PipeContentPhase phase,
        double volumeLitres, double outletPressureKPa)
    {
        if (!double.IsFinite(amountLitres) || !double.IsFinite(volumeLitres) ||
            !double.IsFinite(outletPressureKPa)) return 0;
        double volume = Math.Max(ClearanceVolumeLitres, volumeLitres);
        double pressure = Math.Max(-HydraulicMath.AmbientPressureKPa, outletPressureKPa);
        double retained;
        if (phase == PipeContentPhase.Gas)
        {
            retained = HydraulicMath.GasStandardLitresForGaugePressure(pressure, temperatureC) *
                volume / HydraulicMath.PipeCapacityLitres;
        }
        else
        {
            // Invert the chamber's pressure law. Liquid delivery then tracks
            // piston displacement instead of emptying the chamber in one tick
            // after a tiny contraction creates a large pressure difference.
            retained = volume * (1 + pressure / (pressure <= 0
                ? HydraulicMath.AmbientPressureKPa : LiquidCompressionStiffnessKPa));
        }
        return Math.Max(0, amountLitres - retained);
    }

    public static bool CanDrawInfiniteLiquid(double pipeGaugePressureKPa) =>
        double.IsFinite(pipeGaugePressureKPa) &&
        pipeGaugePressureKPa <= -InfiniteLiquidSourceSuctionKPa;

    public static double InfiniteLiquidIntakeLitres(
        double pipeGaugePressureKPa,
        double remainingPipeCapacityLitres,
        double stepSeconds)
    {
        if (!CanDrawInfiniteLiquid(pipeGaugePressureKPa)) return 0;
        double wanted = HydraulicMath.RequestedTransferLitres(
            -pipeGaugePressureKPa, stepSeconds, PipeContentPhase.Liquid);
        return Math.Min(
            Math.Max(0, double.IsFinite(remainingPipeCapacityLitres)
                ? remainingPipeCapacityLitres
                : 0),
            wanted);
    }

    public static float MechanicalResistance(
        double chamberPressureKPa,
        double crankAngleRadians)
    {
        double angle = double.IsFinite(crankAngleRadians) ? crankAngleRadians : 0;
        double signedPressure = double.IsFinite(chamberPressureKPa)
            ? Math.Clamp(chamberPressureKPa, -HydraulicMath.AmbientPressureKPa, MaximumChamberPressureKPa) : 0;
        double pressure = Math.Abs(signedPressure);
        double sin = Math.Sin(angle);
        double displacedLitresPerRadian = StrokeCapacityLitres * .5 *
            Math.Abs(-sin + VisualCrankRadius * sin * Math.Cos(angle) / RodReach(angle));
        // Normal operation follows pressure * displacement. Above the output
        // service pressure the load grows smoothly and rapidly, without locking
        // the network angle or changing the amount of fluid in the chamber.
        double excess = Math.Max(0, signedPressure / ServicePressureKPa - 1);
        double overload = 1 + 4 * excess * excess;
        double load = pressure * displacedLitresPerRadian * MechanicalResistancePerJoulePerRadian * overload;
        return Math.Clamp(
            BaseMechanicalResistance + (float)load,
            BaseMechanicalResistance,
            MaximumMechanicalResistance);
    }

    public static float DirectionalResistance(double pressure, double angle, double travel)
    {
        if (!double.IsFinite(travel) || travel == 0) return BaseMechanicalResistance;
        double derivative = -Math.Sin(angle) + VisualCrankRadius * Math.Sin(angle) * Math.Cos(angle) / RodReach(angle);
        // Pressure resists contraction; vacuum resists expansion. Releasing a
        // compressed chamber must not brake the expansion half of the cycle.
        if (pressure * derivative * Math.Sign(travel) >= 0) return BaseMechanicalResistance;
        return MechanicalResistance(pressure, angle);
    }

    internal static float SampleStrokeLoad(double angle, double travel, double amount, double temperature,
        PipeContentPhase phase, double outletPressure, double outletRoom, double seconds)
    {
        if (!double.IsFinite(travel) || Math.Abs(travel) < 1e-10)
            return BaseMechanicalResistance;
        const int samples = 8;
        double total = 0;
        double previousVolume = ChamberVolumeLitres(angle);
        for (int i = 0; i < samples; i++)
        {
            double sampleAngle = angle + travel * (i + .5) / samples;
            double volume = ChamberVolumeLitres(sampleAngle);
            double pressure = ChamberPressureKPa(amount, temperature, phase, volume);
            if (volume < previousVolume && outletRoom > 0)
            {
                double released = Math.Min(outletRoom, Math.Min(
                    RequestedDischargeLitres(pressure - outletPressure, seconds / samples, phase),
                    DischargeToEquilibriumLitres(amount, temperature, phase, volume, outletPressure)));
                amount -= released;
                outletRoom -= released;
                pressure = ChamberPressureKPa(amount, temperature, phase, volume);
            }
            total += DirectionalResistance(pressure, sampleAngle, travel);
            previousVolume = volume;
        }
        return (float)(total / samples);
    }
}
