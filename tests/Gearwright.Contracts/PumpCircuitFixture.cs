using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Hydraulics;

namespace Gearwright.Contracts;

internal readonly record struct PumpCircuitResult(
    double DeliveredPerCycle,
    double MaximumChamberLitres,
    double MaximumMassError,
    bool Bounded);

/// <summary>A source -> pipe run -> pump -> pipe run -> open outlet circuit at the server's 5 Hz step.</summary>
internal static class PumpCircuitFixture
{
    public static PumpCircuitResult Run(int inputPipes, int outputPipes, bool legacyVacuum,
        int direction = 1, bool blocked = false, int sourceDrop = 0)
    {
        const int ticksPerCycle = 24;
        const int cycles = 60;
        const int measuredCycles = 20;
        int count = inputPipes + outputPipes;
        double[] amounts = new double[count];
        double[] previous = new double[count];
        int[] heights = new int[count];
        heights[0] = -sourceDrop;
        double[] capacities = Enumerable.Repeat(HydraulicMath.PipeCapacityLitres, count).ToArray();
        double chamber = 0;
        double previousVolume = ReciprocatingPumpMath.ChamberVolumeLitres(0);
        double totalSource = 0;
        double totalOutput = 0;
        double measuredStart = 0;
        double largestChamber = 0;
        double massError = 0;
        bool bounded = true;
        for (int tick = 1; tick <= cycles * ticksPerCycle; tick++)
        {
            double angle = direction * tick * 2 * Math.PI / ticksPerCycle;
            double volume = ReciprocatingPumpMath.ChamberVolumeLitres(angle);
            ReciprocatingPumpStroke stroke = ReciprocatingPumpMath.Stroke(previousVolume, volume);
            previousVolume = volume;
            double chamberPressure = ReciprocatingPumpMath.ChamberPressureKPa(chamber, 20, PipeContentPhase.Liquid, volume);
            double[] pressures = new double[count];
            double[] independent = new double[count];
            for (int i = 0; i < count; i++)
            {
                double strongest = 0;
                double weakest = 0;
                foreach (int neighbor in Neighbors(i, inputPipes, count))
                {
                    strongest = Math.Max(strongest, HydraulicMath.PropagatedLiquidPressure(
                        previous[neighbor], heights[neighbor], heights[i], amounts[neighbor] / 10));
                    weakest = Math.Min(weakest, HydraulicMath.PropagatedLiquidSuction(
                        previous[neighbor], heights[neighbor], heights[i]));
                }
                LiquidPipePressureSnapshot snapshot = new(amounts[i] / 10, strongest, weakest);
                independent[i] = Pressure(snapshot, legacyVacuum);
                if (i == inputPipes - 1)
                    snapshot = snapshot.WithBoundary(ReciprocatingPumpMath.DrivingBoundaryPressureKPa(chamberPressure, stroke, input: true));
                if (i == inputPipes)
                    snapshot = snapshot.WithBoundary(ReciprocatingPumpMath.DrivingBoundaryPressureKPa(chamberPressure, stroke, input: false));
                pressures[i] = Pressure(snapshot, legacyVacuum);
            }

            List<PipeTransferIntent> intents = new();
            for (int i = 0; i + 1 < count; i++)
            {
                if (i == inputPipes - 1) continue;
                double difference = HydraulicMath.HydraulicPotential(pressures[i], heights[i], PipeContentPhase.Liquid) -
                    HydraulicMath.HydraulicPotential(pressures[i + 1], heights[i + 1], PipeContentPhase.Liquid);
                int from = difference >= 0 ? i : i + 1;
                int to = from == i ? i + 1 : i;
                intents.Add(new PipeTransferIntent(from, to, HydraulicMath.RequestedTransferLitres(
                    Math.Abs(difference), .2, PipeContentPhase.Liquid)));
            }
            double[] transfers = PipeFlowSolver.ScaleTransfers(amounts, capacities, intents, HydraulicMath.MaximumTransferFractionPerStep);
            for (int i = 0; i < intents.Count; i++)
            {
                amounts[intents[i].From] -= transfers[i];
                amounts[intents[i].To] += transfers[i];
            }

            if (stroke == ReciprocatingPumpStroke.Suction)
            {
                double received = HydraulicMath.BoundedTransferLitres(independent[inputPipes - 1] - chamberPressure,
                    amounts[inputPipes - 1], chamber, volume, .2, PipeContentPhase.Liquid);
                amounts[inputPipes - 1] -= received;
                chamber += received;
            }
            if (stroke == ReciprocatingPumpStroke.Pressure)
            {
                double provided = HydraulicMath.BoundedTransferLitres(chamberPressure - independent[inputPipes],
                    chamber, amounts[inputPipes], 10, .2, PipeContentPhase.Liquid);
                if (!legacyVacuum)
                    provided = Math.Min(provided, ReciprocatingPumpMath.DischargeToEquilibriumLitres(
                        chamber, 20, PipeContentPhase.Liquid, volume, independent[inputPipes]));
                amounts[inputPipes] += provided;
                chamber -= provided;
            }

            double drawn = ReciprocatingPumpMath.InfiniteLiquidIntakeLitres(pressures[0], 10 - amounts[0], .2);
            amounts[0] += drawn;
            totalSource += drawn;
            if (!blocked)
            {
                double discharged = Math.Min(amounts[^1],
                    (HydraulicMath.NozzleInventoryRateLitresPerSecond + Math.Max(0, pressures[^1]) * .25) * .2);
                amounts[^1] -= discharged;
                totalOutput += discharged;
            }
            bool inputEmpty = amounts.Take(inputPipes).Sum() < .000001;
            bool outputEmpty = amounts.Skip(inputPipes).Sum() < .000001;
            for (int i = 0; i < count; i++)
                previous[i] = legacyVacuum && (i < inputPipes ? inputEmpty : outputEmpty) ? 0 : pressures[i];
            massError = Math.Max(massError, Math.Abs(totalSource - amounts.Sum() - chamber - totalOutput));
            largestChamber = Math.Max(largestChamber, chamber);
            bounded &= amounts.All(amount => amount >= -.000001 && amount <= 10.000001) && chamber >= -.000001;
            if (tick == (cycles - measuredCycles) * ticksPerCycle) measuredStart = totalOutput;
        }
        return new((totalOutput - measuredStart) / measuredCycles, largestChamber, massError, bounded);
    }

    private static double Pressure(LiquidPipePressureSnapshot snapshot, bool legacy)
    {
        if (!legacy) return snapshot.PressureKPa;
        double positive = HydraulicMath.LiquidPressureFromPrevious(snapshot.FillFraction, snapshot.StrongestPressureKPa);
        return snapshot.WeakestSuctionKPa < 0 ? Math.Min(positive, snapshot.WeakestSuctionKPa) : positive;
    }

    private static IEnumerable<int> Neighbors(int index, int inputPipes, int count)
    {
        if (index > 0 && index != inputPipes) yield return index - 1;
        if (index < count - 1 && index != inputPipes - 1) yield return index + 1;
    }
}
