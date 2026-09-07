using System;
using System.IO;
using Gearwright.Hydraulics;
using ProtoBuf;

namespace Gearwright.Contracts;

internal static class PumpTimingFixture
{
    public static void Run(Action<bool, string> check)
    {
        const double radians = Math.PI / 180;
        const float bearing = ReciprocatingPumpMath.BaseMechanicalResistance;
        foreach (int direction in new[] { -1, 1 })
        {
            double angle = direction * 120 * radians;
            check(ReciprocatingPumpMath.DirectionalResistance(1800, angle, direction * .2) > bearing &&
                  ReciprocatingPumpMath.DirectionalResistance(1800, angle, direction * -.2) == bearing &&
                  ReciprocatingPumpMath.DirectionalResistance(-70, angle, direction * .2) == bearing &&
                  ReciprocatingPumpMath.DirectionalResistance(-70, angle, direction * -.2) > bearing,
                $"Pressure only brakes contraction, vacuum only expansion, in direction {direction}");
            float blocked = ReciprocatingPumpMath.SampleStrokeLoad(direction * 100 * radians, direction * .3,
                4, 20, PipeContentPhase.Liquid, 0, 0, .1);
            float open = ReciprocatingPumpMath.SampleStrokeLoad(direction * 100 * radians, direction * .3,
                4, 20, PipeContentPhase.Liquid, 0, 10, .1);
            check(float.IsFinite(blocked) && blocked > open && blocked > bearing,
                $"Upcoming wet travel loads a blocked drive before it steps past contact, direction {direction}");
            check(ReciprocatingPumpMath.SampleStrokeLoad(Math.PI, direction * .3, 4, 20,
                    PipeContentPhase.Liquid, 0, 0, .1) == bearing,
                $"Compressed fluid does not stall the expanding half after bottom dead centre, direction {direction}");

            PumpPresentationTimeline clock = new();
            PumpNetworkFrame start = Frame(1, 0, direction * 95 * radians, direction, 4);
            PumpNetworkFrame end = Frame(2, 100, direction * 120 * radians, direction,
                ReciprocatingPumpMath.ChamberVolumeLitres(direction * 120 * radians));
            end.Pumps[0].OutputOpen = true;
            check(clock.Push(start, 0) && clock.Push(end, 100), "Presentation accepts increasing snapshots");
            bool matched = true, heldUntilContact = true, monotonic = true;
            double previousAmount = 4;
            for (int millis = 100; millis <= 200; millis++)
            {
                clock.Advance(millis);
                matched &= clock.TryPump(0, 0, 0, 0, out PumpPresentation sample) &&
                    Math.Abs(sample.Angle - clock.Angle) < 1e-12 &&
                    Math.Abs(sample.Volume - ReciprocatingPumpMath.ChamberVolumeLitres(sample.Angle)) < 1e-12;
                if (sample.Volume >= 4)
                    heldUntilContact &= sample.Amount == 4 && !sample.OutputOpen;
                monotonic &= sample.Amount <= previousAmount + 1e-12;
                previousAmount = sample.Amount;
            }
            check(matched && heldUntilContact && monotonic,
                $"Liquid, checks and piston share one clock; discharge waits for wet contact, direction {direction}");
            double finalAngle = clock.Angle;
            clock.Advance(20000);
            check(clock.Angle == finalAngle, "A missing packet cannot extrapolate the piston through an unknown stall");
            PumpNetworkFrame stop = Frame(3, 200, direction * 111 * radians, direction * .000001f, 4);
            check(clock.Push(stop, 20100) && clock.Angle == stop.Angle &&
                  clock.TryPump(0, 0, 0, 0, out PumpPresentation stopped) && stopped.Amount == 4 &&
                  !clock.Push(end, 20200) && clock.Angle == stop.Angle,
                "A stall snaps the entire frame and rejects an older delayed motion packet");
        }

        PumpPresentationTimeline phased = new();
        PumpNetworkFrame first = Frame(1, 100, 6.2, 1, 5);
        PumpNetworkFrame next = Frame(2, 200, 6.7, 1, 5);
        first.Pumps = new[] { first.Pumps[0], Sample(1, -6.2 * 2 + 1, -2, 3) };
        next.Pumps = new[] { next.Pumps[0], Sample(1, -6.7 * 2 + 1, -2, 3) };
        phased.Push(first, 100);
        phased.Push(next, 200);
        phased.Advance(250);
        check(phased.TryPump(1, 0, 0, 0, out var second) &&
              Math.Abs(phased.Angle - 6.45) < 1e-12 && Math.Abs(second.Angle - (-6.45 * 2 + 1)) < 1e-12,
            "Shared shaft phase stays continuous across a revolution with independent offsets and signed gear ratios");

        PumpPresentationTimeline identities = new();
        var wet = Frame(1, 100, 2.4, 1, .1);
        var dry = Frame(2, 200, 2.8, 1, 0);
        dry.Pumps[0].Content = "";
        identities.Push(wet, 100);
        identities.Push(dry, 200);
        identities.Advance(250);
        check(identities.TryPump(0, 0, 0, 0, out var half) && half.Content == "game:waterportion" && half.Amount == .1,
            "A content identity transition is not applied ahead of its presentation frame");
        identities.Advance(300);
        check(identities.TryPump(0, 0, 0, 0, out var empty) && empty.Content == "" && empty.Amount == 0,
            "The complete content identity transition appears at its matching frame");
        var malformed = Frame(3, 300, double.NaN, 1, 1);
        check(!identities.Push(malformed, 400), "Non-finite network snapshots are rejected");

        using MemoryStream stream = new();
        Serializer.Serialize(stream, next);
        stream.Position = 0;
        PumpNetworkFrame roundTrip = Serializer.Deserialize<PumpNetworkFrame>(stream);
        check(roundTrip.IsValid && roundTrip.NetworkId == next.NetworkId && roundTrip.Pumps.Length == 2 &&
              roundTrip.Pumps[1].Angle == next.Pumps[1].Angle && roundTrip.Pumps[1].Amount == 3,
            "Combined motion/content snapshots round-trip through the game's protobuf serializer");
    }

    private static PumpNetworkFrame Frame(long sequence, long time, double angle, float speed, double amount) => new()
    {
        NetworkId = 10, Sequence = sequence, ServerMilliseconds = time, Angle = angle, Speed = speed,
        Pumps = new[] { Sample(0, angle, 1, amount) }
    };

    private static PumpPresentation Sample(int x, double angle, double ratio, double amount) => new()
    {
        X = x, Angle = angle, Ratio = ratio, Amount = amount, Content = "game:waterportion", Temperature = 20,
        Volume = ReciprocatingPumpMath.ChamberVolumeLitres(angle), Stroke = (int)ReciprocatingPumpStroke.Pressure
    };
}
