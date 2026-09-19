using System;
using System.IO;
using Gearwright.Hydraulics;
using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Gearwright.Contracts;

internal static class PumpTimingFixture
{
    public static void Run(Action<bool, string> check)
    {
        const double radians = Math.PI / 180;
        const float bearing = ReciprocatingPumpMath.BaseMechanicalResistance;
        foreach (int direction in new[] { -1, 1 })
        {
            PumpPresentationTimeline wrapped = new();
            double maximumError = 0;
            for (int packet = 0; packet < 100; packet++)
            {
                double shaftAngle = direction * packet * .15;
                double localAngle = Math.Atan2(Math.Sin(-shaftAngle), Math.Cos(-shaftAngle));
                var frame = Frame(packet, packet * 100, shaftAngle, direction * .3f,
                    ReciprocatingPumpMath.ChamberVolumeLitres(localAngle));
                frame.Pumps[0].Angle = localAngle;
                frame.Pumps[0].Ratio = -1;
                wrapped.Push(frame, packet * 100);
                for (int ms = 0; ms < 100; ms += 10)
                {
                    wrapped.Advance(packet * 100 + ms);
                    wrapped.TryPump(0, 0, 0, 0, out var sample);
                    maximumError = Math.Max(maximumError, Math.Abs(sample.Amount - sample.Volume));
                }
            }
            Console.WriteLine($"[MEASURE] wrapped journal presentation, direction {direction}: maximum water/piston error {maximumError:F6} L");
            check(maximumError < .02,
                "Real wrapped journal angles keep a flooded chamber against its piston across repeated revolutions");
        }
        check(ReciprocatingPumpMath.PressureTorque(1500, 1) < 0 &&
              ReciprocatingPumpMath.PressureTorque(1500, -1) > 0 &&
              ReciprocatingPumpMath.PressureTorque(-70, 1) > 0 &&
              ReciprocatingPumpMath.PressureTorque(1500, 4) > 0,
            "Signed pressure torque opposes compression and assists expansion, while vacuum reverses the force");
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
            check(clock.TryPump(0, 0, 0, 0, out var held) &&
                  held.Stroke == (int)ReciprocatingPumpStroke.Stationary &&
                  held.Throughput == 0 && !held.IntakeOpen && !held.OutputOpen,
                "An expired motion snapshot closes all checks and stops visible flow with the held piston");
            PumpNetworkFrame stop = Frame(3, 200, direction * 111 * radians, direction * .000001f, 4);
            bool acceptedStop = clock.Push(stop, 20100);
            clock.Advance(20200);
            check(acceptedStop && clock.Angle == stop.Angle &&
                  clock.TryPump(0, 0, 0, 0, out PumpPresentation stopped) && stopped.Amount == 4 &&
                  !clock.Push(end, 20200) && clock.Angle == stop.Angle,
                "A stall settles the entire frame and rejects an older delayed motion packet");
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

        PumpPresentationTimeline jitter = new();
        jitter.Push(Frame(1, 100, -.5, 1, 1), 100);
        jitter.Push(Frame(2, 200, -.1, 1, 1.5), 200);
        jitter.Advance(245);
        jitter.TryPump(0, 0, 0, 0, out var beforeArrival);
        double shownAngle = jitter.Angle;
        jitter.Push(Frame(3, 310, .2, 1, 1.65), 245);
        jitter.TryPump(0, 0, 0, 0, out var afterArrival);
        check(jitter.Angle == shownAngle && Math.Abs(beforeArrival.Amount - afterArrival.Amount) < 1e-12 &&
              beforeArrival.Volume == afterArrival.Volume,
            "A packet crossing top dead centre with receipt/timestamp jitter cannot jump the displayed water or piston");
        jitter.Advance(355);
        check(jitter.TryPump(0, 0, 0, 0, out var caughtUp) && caughtUp.Amount == 1.65 && Math.Abs(jitter.Angle - .2) < 1e-12,
            "The continuous clock still reaches the authoritative contents and angle in one packet interval");

        foreach (int direction in new[] { -1, 1 })
        {
            PumpPresentationTimeline turnaround = new();
            turnaround.Push(Frame(1, 100, direction * -.2, direction, 1), 100);
            turnaround.Push(Frame(2, 200, direction * .2, direction, 1.5), 200);
            turnaround.Advance(250);
            turnaround.TryPump(0, 0, 0, 0, out var top);
            turnaround.Advance(275);
            turnaround.TryPump(0, 0, 0, 0, out var down);
            check(top.Amount == 1.5 && down.Amount == top.Amount,
                "A packet spanning the stroke turnaround finishes showing intake at top dead centre, before the downstroke");
        }

        var intake = Sample(0, 1, 1, 1);
        foreach (int direction in new[] { 1, -1 })
        {
            PumpPresentationTimeline bottomTurn = new();
            bottomTurn.Push(Frame(1, 100, direction * (Math.PI - .2), direction, .1), 100);
            var bottomEnd = Frame(2, 200, direction * (Math.PI + .2), direction, .05);
            bottomEnd.Pumps[0].OutputOpen = true;
            bottomTurn.Push(bottomEnd, 200);
            bottomTurn.Advance(250);
            bottomTurn.TryPump(0, 0, 0, 0, out var atBottom);
            bottomTurn.Advance(275);
            check(bottomTurn.TryPump(0, 0, 0, 0, out var rising) && Math.Abs(atBottom.Amount - .05) < 1e-9 &&
                Math.Abs(rising.Amount - atBottom.Amount) < 1e-9 && !rising.OutputOpen,
                "Discharge completes at bottom dead centre and cannot drain or refill the visible upstroke across a packet boundary");
        }
        PumpPresentationTimeline backdrive = new();
        backdrive.Push(Frame(1, 100, 1.5, -1, 2), 100);
        var release = Frame(2, 200, 1.4, -1, 1.9);
        release.Pumps[0].OutputOpen = true;
        backdrive.Push(release, 200);
        backdrive.Advance(250);
        check(backdrive.TryPump(0, 0, 0, 0, out var pressureRelease) && !pressureRelease.OutputOpen &&
              pressureRelease.Stroke == (int)ReciprocatingPumpStroke.Suction,
            "An outlet event from a packet interval cannot open the check on the visible upstroke");

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

        PumpPresentationTimeline mounting = new();
        mounting.Push(Frame(1, 100, 1, 1, 1), 100);
        check(!mounting.TryPump(1, 0, 0, 0, out _), "A newly placed rod waits for its first authoritative network snapshot");
        var pair = Frame(2, 200, 1.2, 1, 1);
        pair.Pumps = new[] { pair.Pumps[0], Sample(1, 1.2 + Math.PI, 1, 1) };
        pair.Pumps[0].RodOffsetX = -.7f / 16;
        pair.Pumps[1].RodOffsetX = .7f / 16;
        mounting.Push(pair, 200);
        mounting.Advance(225);
        check(mounting.TryPump(0, 0, 0, 0, out var firstSeat) && mounting.TryPump(1, 0, 0, 0, out var secondSeat) &&
            firstSeat.RodOffsetX == -.7f / 16 && secondSeat.RodOffsetX == .7f / 16,
            "Topology regrouping changes all rod seats together, without interpolating them through each other");
        var missingSeat = Frame(3, 300, 1.3, 1, 1);
        check(missingSeat.IsValid && missingSeat.Pumps[0].RodOffsetX == 0,
            "Older motion snapshots without the additive seat field default to a centered rod");
        missingSeat.Pumps[0].RodOffsetX = float.NaN;
        check(!missingSeat.IsValid, "Non-finite rod seats are rejected before rendering");
        missingSeat.Pumps[0].RodOffsetX = 1;
        check(!missingSeat.IsValid, "Out-of-journal rod seats are rejected before rendering");

        using MemoryStream stream = new();
        next.Pumps[0].RodOffsetX = -.7f / 16;
        next.Pumps[1].RodOffsetX = .7f / 16;
        next.Pumps[1].IntakeThroughput = 2.5;
        Serializer.Serialize(stream, next);
        stream.Position = 0;
        PumpNetworkFrame roundTrip = Serializer.Deserialize<PumpNetworkFrame>(stream);
        check(roundTrip.IsValid && roundTrip.NetworkId == next.NetworkId && roundTrip.Pumps.Length == 2 &&
              roundTrip.Pumps[1].Angle == next.Pumps[1].Angle && roundTrip.Pumps[1].Amount == 3 &&
              roundTrip.Pumps[0].RodOffsetX == -.7f / 16 && roundTrip.Pumps[1].RodOffsetX == .7f / 16 &&
              roundTrip.Pumps[1].IntakeThroughput == 2.5,
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
