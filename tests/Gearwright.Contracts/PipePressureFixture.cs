using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Gearwright.Hydraulics;

namespace Gearwright.Contracts;

internal static class PipePressureFixture
{
    public static void Run(Action<bool, string> check)
    {
        FluidCell Cell(double amount, int y = 0, PipeContentPhase phase = PipeContentPhase.Liquid, double temperature = 20) =>
            new(amount, temperature, 10, y, phase);
        foreach (var cell in new[] { Cell(1), Cell(10), Cell(10.1), Cell(40, phase: PipeContentPhase.Gas),
                     Cell(1) with { Suction = -90 }, new FluidCell(4, 20, 3, 0, PipeContentPhase.Liquid, true) })
            check(Math.Abs(cell.AmountAt(cell.Pressure(cell.Amount)) - cell.Amount) < 1e-10,
                "Stored amount and pressure invert without reinterpreting litres or losing compressed contents");

        FluidCell[] full = { new(4, 20, 3.9, 0, PipeContentPhase.Liquid, true), Cell(10), Cell(10), Cell(10) };
        foreach (double sourceSuction in new[] { -82.0, -92, -98 })
        foreach (double fill in new[] { .0, .1, .5, .98, .995, 1.01 })
        {
            var sourceCell = new FluidCell(fill * 4, 20, 4, 0, PipeContentPhase.Liquid, true, sourceSuction);
            double pressure = sourceCell.Pressure(sourceCell.Amount);
            double derivative = (sourceCell.AmountAt(pressure + .0001) - sourceCell.AmountAt(pressure)) / .0001;
            check(Math.Abs(sourceCell.AmountAt(pressure) - sourceCell.Amount) < 1e-10 &&
                Math.Abs(sourceCell.Compliance(pressure) - derivative) < 1e-7 && pressure >= -HydraulicMath.AmbientPressureKPa,
                "Source-aware chamber pressure, inverse and compliance agree through intake and compression");
        }
        FluidLink[] path = { new(0, 1, .5, true), new(1, 2, .5), new(2, 3, .5) };
        FluidBoundary[] outlet = { new(3, 0, .5, false) };
        bool success = PipePressureSolver.TryStep(full, path, outlet, .02, out var first);
        check(success && first.LinkLitres.All(f => f > 0) && first.BoundaryLitres[0] > 0 &&
              Math.Abs(first.Amounts.Sum() + first.BoundaryLitres.Sum() - 34) < 1e-11,
            "A pump displaces a completely full three-pipe line through its outlet in the same step, conserving every litre");
        success = PipePressureSolver.TryStep(full, path, Array.Empty<FluidBoundary>(), .02, out var capped);
        check(success && Math.Abs(capped.Amounts.Sum() - 34) < 1e-11 && capped.Pressures[3] > 2 &&
              capped.Pressures[0] > first.Pressures[0],
            "A capped line retains its liquid and develops more pump back-pressure than an open line");

        FluidLink[] pair = { new(0, 1, .5) };
        success = PipePressureSolver.TryStep(new[] { Cell(8, 1), Cell(2) }, pair, Array.Empty<FluidBoundary>(), .02, out var gravity);
        success &= PipePressureSolver.TryStep(new[] { Cell(8, 0), Cell(2, 1) }, pair, Array.Empty<FluidBoundary>(), .02, out var rise);
        check(success && gravity.LinkLitres[0] > 0 && rise.LinkLitres[0] < 0,
            "Liquid falls under gravity and an unpowered lower reservoir does not force water uphill");
        success = PipePressureSolver.TryStep(new[] { Cell(10.02), Cell(10, 5) }, pair, Array.Empty<FluidBoundary>(), .02, out var lift);
        check(success && lift.LinkLitres[0] > 0, "Compression pressure overcomes the head of a five-block liquid lift");
        success = PipePressureSolver.TryStep(new[] { Cell(4), Cell(4) with { Suction = -80 } }, pair,
            Array.Empty<FluidBoundary>(), .02, out var suction);
        check(success && suction.LinkLitres[0] > 0, "Driven suction draws available liquid toward a partially filled intake");

        foreach (int height in new[] { 0, 50 })
        {
            var gas = new[] { Cell(40, phase: PipeContentPhase.Gas), Cell(2, height, PipeContentPhase.Gas) };
            success = PipePressureSolver.TryStep(gas, pair, Array.Empty<FluidBoundary>(), .2, out var result);
            check(success && result.LinkLitres[0] > 0 && result.Pressures[0] > result.Pressures[1] &&
                  Math.Abs(result.Amounts.Sum() - 42) < 1e-11,
                $"Gas expands into lower pressure, conserves standard litres and does not acquire liquid head at height {height}");
        }
        check(Cell(10).Compliance(100) < Cell(10, phase: PipeContentPhase.Gas).Compliance(100) / 100,
            "Gas storage is far more compressible than a flooded liquid pipe");

        var network = new[] { Cell(10.1, temperature: 90), Cell(10, temperature: -10), Cell(10), Cell(5, 1) };
        FluidLink[] branch = { new(0, 1, .5), new(0, 2, .5), new(1, 3, .5), new(2, 3, .5) };
        double mass = network.Sum(c => c.Amount), energy = network.Sum(c => c.Amount * c.Temperature);
        bool conserved = true;
        for (int tick = 0; tick < 200; tick++)
        {
            if (!PipePressureSolver.TryStep(network, branch, Array.Empty<FluidBoundary>(), .02, out var result)) { conserved = false; break; }
            conserved &= Math.Abs(result.Amounts.Sum() - mass) < 1e-10 &&
                Math.Abs(result.Amounts.Select((amount, i) => amount * result.Temperatures[i]).Sum() - energy) < 1e-8;
            for (int i = 0; i < network.Length; i++) network[i] = network[i] with { Amount = result.Amounts[i], Temperature = result.Temperatures[i] };
        }
        check(conserved, "Branches and loops conserve mass and thermal content, including temperatures below zero");
        success = PipePressureSolver.TryStep(full, path.Reverse().ToArray(), outlet, .02, out var reordered);
        check(success && reordered.Amounts.Zip(first.Amounts).All(p => Math.Abs(p.First - p.Second) < 1e-9),
            "Reordering pipe links does not select a different destination or change the fluid balance");

        var dry = new[] { Cell(0), Cell(0), Cell(0) };
        var dryLinks = new[] { new FluidLink(0, 1, .5), new FluidLink(1, 2, .5) };
        success = PipePressureSolver.TryStep(dry, dryLinks,
            new[] { new FluidBoundary(0, 1000, .5, true, 90) }, .02, out var priming);
        check(success && priming.Amounts[0] > 0 && priming.Amounts[1] == 0 && priming.Amounts[2] == 0 &&
            priming.Temperatures[0] == 90,
            "New contents and heat cannot teleport along an empty run during a pressure solve");
        success = PipePressureSolver.TryStep(new[] { Cell(10.1), Cell(10) }, pair,
            new[] { new FluidBoundary(0, 100, .5, true) }, .02, out var backpressure);
        check(success && backpressure.BoundaryLitres[0] == 0 && backpressure.LinkLitres[0] > 0,
            "An overpressured line blocks a weaker source while retaining and redistributing its stored excess");
        success = PipePressureSolver.TryStep(new[] { Cell(30, phase: PipeContentPhase.Gas) }, Array.Empty<FluidLink>(),
            new[] { new FluidBoundary(0, 0, .25, false) }, .2, out var vent);
        check(success && vent.BoundaryLitres[0] > 0 && vent.Pressures[0] >= 0 &&
            Math.Abs(vent.Amounts[0] + vent.BoundaryLitres[0] - 30) < 1e-12,
            "Gas venting is pressure-balanced and cannot drain below the ambient gas amount");
        check(!PipePressureSolver.TryStep(new[] { Cell(1) }, new[] { new FluidLink(0, 2, .5) },
                Array.Empty<FluidBoundary>(), .02, out _) &&
            !PipePressureSolver.TryStep(new[] { Cell(double.NaN) }, Array.Empty<FluidLink>(),
                Array.Empty<FluidBoundary>(), .02, out _) &&
            !PipePressureSolver.TryStep(new FluidCell[PipePressureSolver.MaximumCells + 1], Array.Empty<FluidLink>(),
                Array.Empty<FluidBoundary>(), .02, out _),
            "Invalid state, invalid connections and oversized components are rejected without mutating storage");

        var large = Enumerable.Range(0, 1024).Select(i => Cell(10)).ToArray();
        var longPath = Enumerable.Range(0, 1023).Select(i => new FluidLink(i, i + 1,
            HydraulicMath.PipeConductanceLitresPerSecondPerKPa)).ToArray();
        var ends = new[] { new FluidBoundary(0, 100, HydraulicMath.PipeConductanceLitresPerSecondPerKPa, true),
            new FluidBoundary(1023, 0, HydraulicMath.AirOutletConductancePerSecondPerKPa, false) };
        Stopwatch watch = Stopwatch.StartNew();
        bool bounded = true;
        for (int tick = 0; tick < 20; tick++)
        {
            if (!PipePressureSolver.TryStep(large, longPath, ends, .02, out var result)) { bounded = false; break; }
            for (int i = 0; i < large.Length; i++) large[i] = large[i] with { Amount = result.Amounts[i] };
        }
        watch.Stop();
        Console.WriteLine($"[MEASURE] 1024-pipe pressure solve, 20 steps: {watch.Elapsed.TotalMilliseconds:F1} ms");
        check(bounded && watch.Elapsed.TotalSeconds < 5, "A large pipe component uses bounded sparse work without a dense matrix");

        // A grid exercises incomplete preconditioning across many loops rather
        // than the exact chain case, at the current higher pipe conductance.
        var grid = Enumerable.Range(0, 256).Select(i => Cell(10, temperature: i % 2 == 0 ? 80 : -10)).ToArray();
        List<FluidLink> gridLinks = new();
        for (int i = 0; i < grid.Length; i++)
        {
            if (i % 16 < 15) gridLinks.Add(new(i, i + 1, HydraulicMath.PipeConductanceLitresPerSecondPerKPa));
            if (i + 16 < grid.Length) gridLinks.Add(new(i, i + 16, HydraulicMath.PipeConductanceLitresPerSecondPerKPa));
        }
        var gridEnds = new[] { new FluidBoundary(0, 100, 8, true, 90), new FluidBoundary(255, 0, 1, false) };
        double expectedMass = grid.Sum(c => c.Amount), expectedEnergy = grid.Sum(c => c.Amount * c.Temperature);
        conserved = true;
        for (int tick = 0; tick < 40; tick++)
        {
            if (!PipePressureSolver.TryStep(grid, gridLinks, gridEnds, .02, out var result)) { conserved = false; break; }
            expectedMass -= result.BoundaryLitres.Sum();
            expectedEnergy -= result.BoundaryLitres[0] * 90 + result.BoundaryLitres[1] * grid[255].Temperature;
            conserved &= Math.Abs(result.Amounts.Sum() - expectedMass) < 1e-9 &&
                Math.Abs(result.Amounts.Select((amount, i) => amount * result.Temperatures[i]).Sum() - expectedEnergy) < 1e-7;
            for (int i = 0; i < grid.Length; i++) grid[i] = grid[i] with { Amount = result.Amounts[i], Temperature = result.Temperatures[i] };
        }
        check(conserved, "A flooded 256-pipe grid converges at faster conductance while conserving liquid and heat");
    }
}
