using System;

namespace Gearwright.Hydraulics;

/// <summary>One liquid pipe's pressure inputs before a pump applies its own boundary.</summary>
public readonly record struct LiquidPipePressureSnapshot(
    double FillFraction,
    double StrongestPressureKPa,
    double WeakestSuctionKPa)
{
    public double PressureKPa
    {
        get
        {
            double pressure = HydraulicMath.LiquidPressureFromPrevious(FillFraction, StrongestPressureKPa);
            if (!double.IsFinite(WeakestSuctionKPa) || WeakestSuctionKPa >= 0) return pressure;
            // Empty space transmits the vacuum used to prime a dry run. As water
            // arrives it must recover local pressure, or every wet pipe stays
            // pinned to the same vacuum and flow is only the propagation loss.
            double empty = 1 - Math.Clamp(double.IsFinite(FillFraction) ? FillFraction : 0, 0, 1);
            return pressure + empty * empty * (Math.Min(pressure, WeakestSuctionKPa) - pressure);
        }
    }

    public LiquidPipePressureSnapshot WithBoundary(double pressureKPa) =>
        !double.IsFinite(pressureKPa) ? this : this with
        {
            StrongestPressureKPa = Math.Max(StrongestPressureKPa, pressureKPa),
            WeakestSuctionKPa = Math.Min(WeakestSuctionKPa, pressureKPa)
        };
}

/// <summary>Stable hydraulic tuning shared by simulation and contract tests.</summary>
public static class HydraulicMath
{
    public const double PipeCapacityLitres = 10;
    public const double AmbientPressureKPa = 101.325;
    public const double ReferenceTemperatureKelvin = 293.15;
    public const double WaterHeadKPaPerBlock = 9.80665;
    public const double LiquidFullnessPressureKPa = 2;
    public const double EmptyPipeSuctionKPa = 0.35;
    public const double PressurePropagationLossKPa = 0.2;
    public const double PipeConductanceLitresPerSecondPerKPa = 8;
    public const double GasConductanceStandardLitresPerSecondPerKPa = 1.6;
    public const double AirOutletConductancePerSecondPerKPa = 1;
    public const double FlowDeadbandKPa = 0.01;
    public const double MaximumTransferFractionPerStep = 0.45;
    public const double NozzleInventoryRateLitresPerSecond = 8;

    public const double FullSprinklerPressure = 100;
    public const double MaximumCreativePressure = 1000;
    public const double FullSprinklerLitresPerDay = 480;
    public const double FullIrrigatorPressure = 20;
    public const double FullIrrigatorLitresPerDay = 72;
    public const double IrrigatorMaximumMoisture = 0.8;

    // These values currently drive warning audio only. Pipes do not fail yet.
    public const double PressureWarningStartKPa = 100;
    public const double AssumedPipeFailurePressureKPa = 150;
    public const double ExtremeAudioFlowLitresPerSecond = 8;
    public const double DistantAudioIntensityThreshold = 0.75;
    public const double LocalAmbientAudioRangeBlocks = 2.8;
    public const double ExtremeAmbientAudioRangeBlocks = 16;

    public static double Performance(double consumerPressure) =>
        Math.Clamp(consumerPressure / FullSprinklerPressure, 0, 1);

    public static double IrrigatorPerformance(double consumerPressure) =>
        Math.Clamp(consumerPressure / FullIrrigatorPressure, 0, 1);

    public static int Reach(double consumerPressure)
    {
        if (consumerPressure < 1) return 0;
        if (consumerPressure < 25) return 1;
        if (consumerPressure < 50) return 2;
        if (consumerPressure < 75) return 3;
        return 4;
    }

    public static double LitresPerDayPerConsumer(double consumerPressure) =>
        FullSprinklerLitresPerDay * Math.Sqrt(Performance(consumerPressure));

    public static double IrrigatorLitresPerDay(double consumerPressure) =>
        FullIrrigatorLitresPerDay * Math.Sqrt(IrrigatorPerformance(consumerPressure));

    public static double PressureWarningIntensity(double pressureKPa)
    {
        double pressure = double.IsFinite(pressureKPa) ? pressureKPa : 0;
        return Math.Clamp(
            (pressure - PressureWarningStartKPa) /
                (AssumedPipeFailurePressureKPa - PressureWarningStartKPa),
            0,
            1);
    }

    public static double AudioFlowIntensity(double litresPerSecond)
    {
        double flow = Math.Abs(double.IsFinite(litresPerSecond) ? litresPerSecond : 0);
        return Math.Sqrt(Math.Clamp(flow / ExtremeAudioFlowLitresPerSecond, 0, 1));
    }

    public static double AmbientAudioRange(double intensity)
    {
        double level = Math.Clamp(double.IsFinite(intensity) ? intensity : 0, 0, 1);
        if (level <= DistantAudioIntensityThreshold) return LocalAmbientAudioRangeBlocks;
        double distant = (level - DistantAudioIntensityThreshold) /
            (1 - DistantAudioIntensityThreshold);
        return LocalAmbientAudioRangeBlocks +
            (ExtremeAmbientAudioRangeBlocks - LocalAmbientAudioRangeBlocks) * distant * distant;
    }

    public static double GasGaugePressure(double standardLitres, double temperatureC)
        => GasGaugePressureForVolume(standardLitres, temperatureC, PipeCapacityLitres);

    public static double GasGaugePressureForVolume(
        double standardLitres,
        double temperatureC,
        double volumeLitres)
    {
        double amount = Math.Max(0, double.IsFinite(standardLitres) ? standardLitres : 0);
        double kelvin = Math.Max(1,
            double.IsFinite(temperatureC) ? temperatureC + 273.15 : ReferenceTemperatureKelvin);
        double volume = Math.Max(0.000001,
            double.IsFinite(volumeLitres) ? volumeLitres : PipeCapacityLitres);
        double absolutePressure = AmbientPressureKPa *
            (amount / volume) * (kelvin / ReferenceTemperatureKelvin);
        return Math.Max(-AmbientPressureKPa, absolutePressure - AmbientPressureKPa);
    }

    public static double GasStandardLitresForGaugePressure(double gaugePressureKPa, double temperatureC)
    {
        double gauge = Math.Max(-AmbientPressureKPa,
            double.IsFinite(gaugePressureKPa) ? gaugePressureKPa : 0);
        double kelvin = Math.Max(1,
            double.IsFinite(temperatureC) ? temperatureC + 273.15 : ReferenceTemperatureKelvin);
        return PipeCapacityLitres * ((gauge + AmbientPressureKPa) / AmbientPressureKPa) *
            (ReferenceTemperatureKelvin / kelvin);
    }

    public static double GasVentableStandardLitres(double standardLitres, double temperatureC) =>
        Math.Max(0, (double.IsFinite(standardLitres) ? standardLitres : 0) -
            GasStandardLitresForGaugePressure(0, temperatureC));

    public static double LiquidOverflowLitres(double litres) =>
        Math.Max(0, (double.IsFinite(litres) ? litres : 0) - PipeCapacityLitres);

    public static double StoredPressure(double amount, double temperature, PipeContentPhase phase, double suction = 0)
    {
        if (phase == PipeContentPhase.Gas) return GasGaugePressure(amount, temperature);
        double fill = Math.Max(0, amount) / PipeCapacityLitres;
        if (fill > 1) return LiquidFullnessPressureKPa +
            (fill - 1) * ReciprocatingPumpMath.LiquidCompressionStiffnessKPa;
        return LiquidBasePressure(fill) + Math.Clamp(suction, -AmbientPressureKPa, 0) * (1 - fill) * (1 - fill);
    }

    public static double LiquidBasePressure(double fillFraction)
    {
        double fill = Math.Clamp(double.IsFinite(fillFraction) ? fillFraction : 0, 0, 1);
        double fullness = LiquidFullnessPressureKPa * fill * fill;
        double suction = EmptyPipeSuctionKPa * (1 - fill) * (1 - fill);
        return fullness - suction;
    }

    public static double LiquidPressureFromPrevious(double fillFraction, double strongestGeneratedPressureKPa)
    {
        double fill = Math.Clamp(double.IsFinite(fillFraction) ? fillFraction : 0, 0, 1);
        double generated = double.IsFinite(strongestGeneratedPressureKPa)
            ? strongestGeneratedPressureKPa
            : 0;
        double local = LiquidBasePressure(fill);
        double transmitted = Math.Max(local, generated);
        return local + fill * fill * (transmitted - local);
    }

    public static double PropagatedLiquidPressure(
        double previousNeighborPressureKPa,
        int neighborY,
        int ownY,
        double neighborFillFraction = 1)
    {
        double previous = double.IsFinite(previousNeighborPressureKPa)
            ? previousNeighborPressureKPa
            : 0;
        double fill = Math.Clamp(
            double.IsFinite(neighborFillFraction) ? neighborFillFraction : 0, 0, 1);
        double retained = Math.Max(0, previous - PressurePropagationLossKPa);
        return fill * fill * (retained + (neighborY - ownY) * WaterHeadKPaPerBlock);
    }

    public static double PropagatedLiquidSuction(
        double previousNeighborPressureKPa,
        int neighborY,
        int ownY)
    {
        double previous = double.IsFinite(previousNeighborPressureKPa)
            ? previousNeighborPressureKPa
            : 0;
        // The small empty-pipe pooling bias is not pump-generated vacuum.
        // In particular it must not become stronger up a dry vertical run.
        if (previous >= -EmptyPipeSuctionKPa) return 0;
        double weakened = Math.Min(0, previous + PressurePropagationLossKPa);
        // Suction travelling down from a higher pump pays the lift head. Going
        // upward cannot create extra suction; gravity already acts on flow via
        // HydraulicPotential and on positive pressure below a liquid column.
        double liftHead = Math.Max(0, (double)neighborY - ownY) * WaterHeadKPaPerBlock;
        return Math.Clamp(
            weakened + liftHead,
            -AmbientPressureKPa,
            0);
    }

    public static double LiquidContainerPressure(
        double fillFraction,
        double containerBottomY,
        double nozzleY)
    {
        double fill = Math.Clamp(double.IsFinite(fillFraction) ? fillFraction : 0, 0, 1);
        double surfaceY = containerBottomY + fill;
        return (surfaceY - nozzleY) * WaterHeadKPaPerBlock;
    }

    public static double HydraulicPotential(double pressureKPa, int y, PipeContentPhase phase) =>
        (double.IsFinite(pressureKPa) ? pressureKPa : 0) +
        (phase == PipeContentPhase.Liquid ? y * WaterHeadKPaPerBlock : 0);

    public static double RequestedTransferLitres(
        double pressureDifferenceKPa,
        double stepSeconds,
        PipeContentPhase phase)
    {
        double difference = Math.Max(0,
            (double.IsFinite(pressureDifferenceKPa) ? pressureDifferenceKPa : 0) - FlowDeadbandKPa);
        double seconds = Math.Max(0, double.IsFinite(stepSeconds) ? stepSeconds : 0);
        double conductance = phase == PipeContentPhase.Gas
            ? GasConductanceStandardLitresPerSecondPerKPa
            : PipeConductanceLitresPerSecondPerKPa;
        return difference * conductance * seconds;
    }

    public static double BoundedTransferLitres(
        double pressureDifferenceKPa,
        double donorAmountLitres,
        double receiverAmountLitres,
        double receiverCapacityLitres,
        double stepSeconds,
        PipeContentPhase phase)
    {
        double available = double.IsFinite(donorAmountLitres) ? Math.Max(0, donorAmountLitres) : 0;
        double stored = double.IsFinite(receiverAmountLitres) ? Math.Max(0, receiverAmountLitres) : 0;
        double room = double.IsPositiveInfinity(receiverCapacityLitres)
            ? double.PositiveInfinity
            : double.IsFinite(receiverCapacityLitres) ? Math.Max(0, receiverCapacityLitres - stored) : 0;
        return Math.Min(Math.Min(available, room),
            RequestedTransferLitres(pressureDifferenceKPa, stepSeconds, phase));
    }

    public static double TextureScrollCyclesPerSecond(
        double actualFlowLitresPerSecond,
        PipeContentPhase phase)
    {
        double flow = Math.Max(0,
            double.IsFinite(actualFlowLitresPerSecond) ? actualFlowLitresPerSecond : 0);
        if (flow <= 0) return 0;
        double conductance = phase == PipeContentPhase.Gas
            ? GasConductanceStandardLitresPerSecondPerKPa
            : PipeConductanceLitresPerSecondPerKPa;
        double effectivePressureDifference = flow / conductance + FlowDeadbandKPa;
        return Math.Min(2.5, 0.16 * Math.Sqrt(effectivePressureDifference));
    }

    public static bool IsInsideCircularReach(int deltaX, int deltaZ, int reach) =>
        reach > 0 && deltaX * deltaX + deltaZ * deltaZ <= reach * reach;
}
