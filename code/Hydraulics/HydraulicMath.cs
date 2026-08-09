using System;

namespace Gearwright.Hydraulics;

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
    public const double PipeConductanceLitresPerSecondPerKPa = 0.5;
    public const double GasConductanceStandardLitresPerSecondPerKPa = 0.1;
    public const double FlowDeadbandKPa = 0.01;
    public const double MaximumTransferFractionPerStep = 0.45;
    public const double NozzleInventoryRateLitresPerSecond = 2;

    public const double FullSprinklerPressure = 100;
    public const double MaximumCreativePressure = 1000;
    public const double FullSprinklerLitresPerDay = 8;

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

    public static double GasGaugePressure(double standardLitres, double temperatureC)
    {
        double amount = Math.Max(0, double.IsFinite(standardLitres) ? standardLitres : 0);
        double kelvin = Math.Max(1,
            double.IsFinite(temperatureC) ? temperatureC + 273.15 : ReferenceTemperatureKelvin);
        double absolutePressure = AmbientPressureKPa *
            (amount / PipeCapacityLitres) * (kelvin / ReferenceTemperatureKelvin);
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
