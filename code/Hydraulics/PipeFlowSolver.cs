using System;
using System.Collections.Generic;

namespace Gearwright.Hydraulics;

public readonly record struct PipeTransferIntent(int From, int To, double RequestedLitres);

/// <summary>Order-independent donor/receiver scaling for one immutable pipe snapshot.</summary>
public static class PipeFlowSolver
{
    public static double[] ScaleTransfers(
        IReadOnlyList<double> availableLitres,
        IReadOnlyList<double> capacityLitres,
        IReadOnlyList<PipeTransferIntent> intents,
        double maximumDonorFraction)
    {
        if (availableLitres.Count != capacityLitres.Count)
            throw new ArgumentException("Availability and capacity lists must have the same length.");
        int nodes = availableLitres.Count;
        double fraction = Math.Clamp(
            double.IsFinite(maximumDonorFraction) ? maximumDonorFraction : 0, 0, 1);
        int[] intentOrder = new int[intents.Count];
        for (int i = 0; i < intents.Count; i++)
        {
            Validate(intents[i], nodes);
            intentOrder[i] = i;
        }
        Array.Sort(intentOrder, (left, right) => Compare(intents[left], intents[right]));

        double[] donorTotals = new double[nodes];
        foreach (int intentIndex in intentOrder)
        {
            PipeTransferIntent intent = intents[intentIndex];
            donorTotals[intent.From] += Requested(intent);
        }

        double[] donorScales = new double[nodes];
        for (int node = 0; node < nodes; node++)
        {
            double available = Math.Max(0,
                double.IsFinite(availableLitres[node]) ? availableLitres[node] : 0);
            double maximum = available * fraction;
            donorScales[node] = donorTotals[node] <= 0 ? 1 : Math.Min(1, maximum / donorTotals[node]);
        }

        double[] receiverTotals = new double[nodes];
        foreach (int intentIndex in intentOrder)
        {
            PipeTransferIntent intent = intents[intentIndex];
            receiverTotals[intent.To] += Requested(intent) * donorScales[intent.From];
        }

        double[] receiverScales = new double[nodes];
        for (int node = 0; node < nodes; node++)
        {
            double capacity = capacityLitres[node];
            double available = Math.Max(0,
                double.IsFinite(availableLitres[node]) ? availableLitres[node] : 0);
            double headroom = double.IsPositiveInfinity(capacity)
                ? double.PositiveInfinity
                : Math.Max(0, (double.IsFinite(capacity) ? capacity : 0) - available);
            receiverScales[node] = receiverTotals[node] <= 0 || double.IsPositiveInfinity(headroom)
                ? 1
                : Math.Min(1, headroom / receiverTotals[node]);
        }

        double[] result = new double[intents.Count];
        for (int i = 0; i < intents.Count; i++)
        {
            PipeTransferIntent intent = intents[i];
            result[i] = Requested(intent) *
                donorScales[intent.From] * receiverScales[intent.To];
        }
        return result;
    }

    private static void Validate(PipeTransferIntent intent, int nodes)
    {
        if (intent.From < 0 || intent.From >= nodes || intent.To < 0 || intent.To >= nodes ||
            intent.From == intent.To)
            throw new ArgumentOutOfRangeException(nameof(intent), "Transfer endpoints must name different nodes.");
    }

    private static double Requested(PipeTransferIntent intent) =>
        double.IsFinite(intent.RequestedLitres) ? Math.Max(0, intent.RequestedLitres) : 0;

    private static int Compare(PipeTransferIntent left, PipeTransferIntent right)
    {
        int comparison = left.From.CompareTo(right.From);
        if (comparison != 0) return comparison;
        comparison = left.To.CompareTo(right.To);
        return comparison != 0 ? comparison : Requested(left).CompareTo(Requested(right));
    }
}
