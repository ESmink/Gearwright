using System;
using System.Collections.Generic;

namespace Gearwright.Hydraulics;

/// <summary>Stored litres retain their save meaning; compression and pressure are derived.</summary>
internal readonly record struct FluidCell(double Amount, double Temperature, double Volume,
    int Y, PipeContentPhase Phase, bool Pump = false, double Suction = 0)
{
    public double Head => Phase == PipeContentPhase.Liquid ? Y * HydraulicMath.WaterHeadKPaPerBlock : 0;
    public double Pressure(double amount) => Pump
        ? ReciprocatingPumpMath.ChamberPressureKPa(amount, Temperature, Phase, Volume)
        : HydraulicMath.StoredPressure(amount, Temperature, Phase, Suction);

    public double AmountAt(double pressure)
    {
        if (Phase == PipeContentPhase.Gas)
            return Math.Max(0, (pressure + HydraulicMath.AmbientPressureKPa) * Volume /
                (HydraulicMath.AmbientPressureKPa * Math.Max(1, Temperature + 273.15) / HydraulicMath.ReferenceTemperatureKelvin));
        double full = Pump ? 0 : HydraulicMath.LiquidFullnessPressureKPa;
        if (pressure >= full)
            return Volume * (1 + (pressure - full) / ReciprocatingPumpMath.LiquidCompressionStiffnessKPa);
        if (Pump) return Math.Max(0, Volume * (1 + pressure / HydraulicMath.AmbientPressureKPa));
        double suction = Math.Clamp(Suction, -HydraulicMath.AmbientPressureKPa, 0);
        double a = HydraulicMath.LiquidFullnessPressureKPa - HydraulicMath.EmptyPipeSuctionKPa + suction;
        double b = 2 * (HydraulicMath.EmptyPipeSuctionKPa - suction);
        double aboveEmpty = Math.Max(0, pressure + HydraulicMath.EmptyPipeSuctionKPa - suction);
        double fill = 2 * aboveEmpty / (b + Math.Sqrt(Math.Max(0, b * b + 4 * a * aboveEmpty)));
        return Volume * Math.Clamp(fill, 0, 1);
    }

    public double Compliance(double pressure)
    {
        if (Phase == PipeContentPhase.Gas)
            return Volume / (HydraulicMath.AmbientPressureKPa * Math.Max(1, Temperature + 273.15) / HydraulicMath.ReferenceTemperatureKelvin);
        if (pressure >= (Pump ? 0 : HydraulicMath.LiquidFullnessPressureKPa))
            return Volume / ReciprocatingPumpMath.LiquidCompressionStiffnessKPa;
        if (Pump) return Volume / HydraulicMath.AmbientPressureKPa;
        double fill = AmountAt(pressure) / Volume;
        double suction = Math.Clamp(Suction, -HydraulicMath.AmbientPressureKPa, 0);
        return Volume / (2 * HydraulicMath.LiquidFullnessPressureKPa * fill +
            2 * (HydraulicMath.EmptyPipeSuctionKPa - suction) * (1 - fill));
    }
}

internal readonly record struct FluidLink(int From, int To, double Conductance,
    bool OneWay = false, double MaximumLitres = double.PositiveInfinity);

// A boundary exchanges with the outside, never with an untracked imaginary pipe.
internal readonly record struct FluidBoundary(int Node, double Pressure, double Conductance,
    bool Source, double Temperature = 20, double MaximumLitres = double.PositiveInfinity);

internal sealed record PressureFlowResult(double[] Amounts, double[] Temperatures,
    double[] Pressures, double[] LinkLitres, double[] BoundaryLitres, int Iterations);

/// <summary>
/// Backward-Euler pressure/storage balance, solved with a sparse Newton/PCG iteration.
/// Full receivers may simultaneously send and receive. Every final change is an edge
/// flux; no amount is clipped to capacity and no liquid skips a connected edge.
/// </summary>
internal static class PipePressureSolver
{
    public const int MaximumCells = 4096;
    private const int MaximumNewtonIterations = 48;
    private const int MaximumLinearIterations = 128;
    private const double MassTolerance = 1e-8;

    public static bool TryStep(IReadOnlyList<FluidCell> cells, IReadOnlyList<FluidLink> links,
        IReadOnlyList<FluidBoundary> boundaries, double seconds, out PressureFlowResult result)
    {
        result = null!;
        int n = cells.Count;
        if (n == 0 || n > MaximumCells || !double.IsFinite(seconds) || seconds <= 0 || seconds > .2) return false;
        int[] degree = new int[n];
        foreach (var edge in links)
        {
            if ((uint)edge.From >= n || (uint)edge.To >= n || edge.From == edge.To ||
                !double.IsFinite(edge.Conductance) || edge.Conductance < 0 ||
                double.IsNaN(edge.MaximumLitres) || edge.MaximumLitres < 0) return false;
            degree[edge.From]++; degree[edge.To]++;
        }
        foreach (var boundary in boundaries)
        {
            if ((uint)boundary.Node >= n || !double.IsFinite(boundary.Pressure) ||
                !double.IsFinite(boundary.Temperature) || !double.IsFinite(boundary.Conductance) ||
                boundary.Conductance < 0 || double.IsNaN(boundary.MaximumLitres) || boundary.MaximumLitres < 0) return false;
            degree[boundary.Node]++;
        }
        // Sparse incomplete LDL preconditioning is exact for an ordered pipe
        // chain and remains bounded for branches/loops. It avoids losing
        // convergence as fast flooded runs become strongly pressure-coupled.
        int[] lowerStart = new int[n + 1];
        foreach (var edge in links) lowerStart[Math.Max(edge.From, edge.To) + 1]++;
        for (int i = 1; i <= n; i++) lowerStart[i] += lowerStart[i - 1];
        int[] lowerCursor = (int[])lowerStart.Clone(), lowerLinks = new int[links.Count];
        for (int k = 0; k < links.Count; k++) lowerLinks[lowerCursor[Math.Max(links[k].From, links[k].To)]++] = k;
        double[] pressure = new double[n], diagonal = new double[n], residual = new double[n];
        double[] edgeWeights = new double[links.Count], fluxes = new double[links.Count];
        double[] boundaryFluxes = new double[boundaries.Count];
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(cells[i].Amount) || cells[i].Amount < 0 ||
                !double.IsFinite(cells[i].Temperature) || !double.IsFinite(cells[i].Volume) ||
                cells[i].Volume <= 0 || !double.IsFinite(cells[i].Suction)) return false;
            pressure[i] = cells[i].Pressure(cells[i].Amount);
        }
        // Donor budgets include every incident edge. This also bounds thermal
        // transport to old contents, so a hot injection cannot teleport its heat.
        double Budget(int node) => cells[node].Amount * (cells[node].Pump ? 1 : HydraulicMath.MaximumTransferFractionPerStep) /
            Math.Max(1, degree[node]);
        double Evaluate(double[] p, bool jacobian)
        {
            for (int i = 0; i < n; i++)
            {
                residual[i] = cells[i].AmountAt(p[i]) - cells[i].Amount;
                if (jacobian) diagonal[i] = cells[i].Compliance(p[i]);
            }
            for (int k = 0; k < links.Count; k++)
            {
                FluidLink edge = links[k];
                double scale = seconds * edge.Conductance;
                double raw = scale * (p[edge.From] + cells[edge.From].Head - p[edge.To] - cells[edge.To].Head);
                double min = edge.OneWay ? 0 : -Budget(edge.To);
                double max = Math.Min(Budget(edge.From), edge.MaximumLitres);
                double flux = Math.Clamp(raw, min, max);
                fluxes[k] = flux;
                residual[edge.From] += flux;
                residual[edge.To] -= flux;
                if (jacobian)
                {
                    // Use the flowing derivative at a valve/budget corner.
                    // Roundoff just outside that corner must not leave Newton
                    // using a closed-valve slope while it tries to reopen it.
                    // Flux itself still uses the exact conservative limits.
                    double weight = raw >= min - MassTolerance && raw <= max + MassTolerance ? scale : 0;
                    edgeWeights[k] = weight;
                    diagonal[edge.From] += weight;
                    diagonal[edge.To] += weight;
                }
            }
            for (int k = 0; k < boundaries.Count; k++)
            {
                FluidBoundary boundary = boundaries[k];
                double raw = seconds * boundary.Conductance * (p[boundary.Node] - boundary.Pressure);
                double min = boundary.Source ? -boundary.MaximumLitres : 0;
                double max = boundary.Source ? 0 : Math.Min(Budget(boundary.Node), boundary.MaximumLitres);
                double flux = Math.Clamp(raw, min, max);
                boundaryFluxes[k] = flux;
                residual[boundary.Node] += flux;
                if (jacobian && raw >= min - MassTolerance && raw <= max + MassTolerance)
                    diagonal[boundary.Node] += seconds * boundary.Conductance;
            }
            double norm = 0;
            foreach (double value in residual) norm = Math.Max(norm, Math.Abs(value));
            return norm;
        }

        double[] delta = new double[n], r = new double[n], z = new double[n], direction = new double[n], product = new double[n];
        double[] pivots = new double[n];
        void Precondition(double[] input, double[] output)
        {
            Array.Copy(input, output, n);
            for (int i = 0; i < n; i++)
                for (int k = lowerStart[i]; k < lowerStart[i + 1]; k++)
                {
                    int edge = lowerLinks[k], j = Math.Min(links[edge].From, links[edge].To);
                    output[i] += edgeWeights[edge] / pivots[j] * output[j];
                }
            for (int i = 0; i < n; i++) output[i] /= pivots[i];
            for (int i = n - 1; i >= 0; i--)
                for (int k = lowerStart[i]; k < lowerStart[i + 1]; k++)
                {
                    int edge = lowerLinks[k], j = Math.Min(links[edge].From, links[edge].To);
                    output[j] += edgeWeights[edge] / pivots[j] * output[i];
                }
        }
        double[] trial = new double[n];
        int iteration;
        for (iteration = 0; iteration < MaximumNewtonIterations; iteration++)
        {
            double error = Evaluate(pressure, true);
            if (!double.IsFinite(error)) return false;
            if (error < MassTolerance) break;
            Array.Clear(delta);
            for (int i = 0; i < n; i++)
            {
                pivots[i] = diagonal[i];
                for (int k = lowerStart[i]; k < lowerStart[i + 1]; k++)
                {
                    int edge = lowerLinks[k], j = Math.Min(links[edge].From, links[edge].To);
                    pivots[i] -= edgeWeights[edge] * edgeWeights[edge] / pivots[j];
                }
                if (pivots[i] <= 0 || !double.IsFinite(pivots[i])) return false;
                r[i] = -residual[i];
            }
            Precondition(r, z);
            double rz = 0;
            for (int i = 0; i < n; i++)
            {
                direction[i] = z[i];
                rz += r[i] * z[i];
            }
            for (int cg = 0; cg < MaximumLinearIterations && rz > 1e-24; cg++)
            {
                for (int i = 0; i < n; i++) product[i] = diagonal[i] * direction[i];
                for (int k = 0; k < links.Count; k++)
                {
                    var edge = links[k];
                    product[edge.From] -= edgeWeights[k] * direction[edge.To];
                    product[edge.To] -= edgeWeights[k] * direction[edge.From];
                }
                double denominator = 0;
                for (int i = 0; i < n; i++) denominator += direction[i] * product[i];
                if (denominator <= 0 || !double.IsFinite(denominator)) break;
                double alpha = rz / denominator, nextRz = 0;
                for (int i = 0; i < n; i++)
                {
                    delta[i] += alpha * direction[i];
                    r[i] -= alpha * product[i];
                }
                Precondition(r, z);
                for (int i = 0; i < n; i++) nextRz += r[i] * z[i];
                double beta = nextRz / rz;
                for (int i = 0; i < n; i++) direction[i] = z[i] + beta * direction[i];
                rz = nextRz;
            }
            bool accepted = false;
            // A nearly empty liquid cylinder is very stiff. A saturated valve
            // may need a small step before its flowing derivative becomes active.
            for (double damping = 1; damping >= 1.0 / (1L << 40); damping *= .5)
            {
                for (int i = 0; i < n; i++) trial[i] = Math.Max(cells[i].Pressure(0), pressure[i] + damping * delta[i]);
                if (Evaluate(trial, false) < error)
                {
                    (pressure, trial) = (trial, pressure);
                    accepted = true;
                    break;
                }
            }
            if (!accepted) return false;
        }
        if (Evaluate(pressure, false) >= MassTolerance) return false;

        double[] amounts = new double[n], energy = new double[n], temperatures = new double[n];
        for (int i = 0; i < n; i++) { amounts[i] = cells[i].Amount; energy[i] = amounts[i] * cells[i].Temperature; }
        for (int k = 0; k < links.Count; k++)
        {
            var edge = links[k];
            double flux = fluxes[k];
            double heat = flux * cells[flux >= 0 ? edge.From : edge.To].Temperature;
            amounts[edge.From] -= flux; amounts[edge.To] += flux;
            energy[edge.From] -= heat; energy[edge.To] += heat;
        }
        for (int k = 0; k < boundaries.Count; k++)
        {
            var boundary = boundaries[k];
            double flux = boundaryFluxes[k];
            amounts[boundary.Node] -= flux;
            energy[boundary.Node] -= flux * (flux >= 0 ? cells[boundary.Node].Temperature : boundary.Temperature);
        }
        for (int i = 0; i < n; i++)
        {
            if (!double.IsFinite(amounts[i]) || amounts[i] < 0) return false;
            temperatures[i] = amounts[i] > 0 ? energy[i] / amounts[i] : cells[i].Temperature;
            pressure[i] = (cells[i] with { Temperature = temperatures[i] }).Pressure(amounts[i]);
        }
        result = new(amounts, temperatures, pressure, fluxes, boundaryFluxes, iteration);
        return true;
    }
}
