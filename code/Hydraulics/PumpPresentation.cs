using System;
using System.Linq;
using ProtoBuf;

namespace Gearwright.Hydraulics;

// Wire-only snapshots: none of these fields is written into a world save.
[ProtoContract]
public sealed class PumpNetworkFrame
{
    [ProtoMember(1)] public int Version { get; set; } = 1;
    [ProtoMember(2)] public long NetworkId { get; set; }
    [ProtoMember(3)] public long Sequence { get; set; }
    [ProtoMember(4)] public long ServerMilliseconds { get; set; }
    [ProtoMember(5)] public double Angle { get; set; }
    [ProtoMember(6)] public float Speed { get; set; }
    [ProtoMember(7)] public PumpPresentation[] Pumps { get; set; } = Array.Empty<PumpPresentation>();

    internal bool IsValid => Version == 1 && Sequence >= 0 && ServerMilliseconds >= 0 &&
        double.IsFinite(Angle) && float.IsFinite(Speed) && Pumps != null &&
        Pumps.Length <= 4096 && Pumps.All(pump => pump != null && pump.IsValid);
}

[ProtoContract]
public sealed class PumpPresentation
{
    [ProtoMember(1)] public int X { get; set; }
    [ProtoMember(2)] public int Y { get; set; }
    [ProtoMember(3)] public int Z { get; set; }
    [ProtoMember(4)] public int Dimension { get; set; }
    [ProtoMember(5)] public double Angle { get; set; }
    [ProtoMember(6)] public double Ratio { get; set; }
    [ProtoMember(7)] public double Amount { get; set; }
    [ProtoMember(8)] public string Content { get; set; } = "";
    [ProtoMember(9)] public double Temperature { get; set; }
    [ProtoMember(10)] public double Volume { get; set; }
    [ProtoMember(11)] public double Throughput { get; set; }
    [ProtoMember(12)] public int Stroke { get; set; }
    [ProtoMember(13)] public bool IntakeOpen { get; set; }
    [ProtoMember(14)] public bool OutputOpen { get; set; }
    [ProtoMember(15)] public float RodOffsetX { get; set; }
    [ProtoMember(16)] public double IntakeThroughput { get; set; }

    internal bool IsValid => double.IsFinite(Angle) && double.IsFinite(Ratio) &&
        double.IsFinite(Amount) && Amount >= 0 && double.IsFinite(Temperature) &&
        double.IsFinite(Volume) && Volume >= ReciprocatingPumpMath.ClearanceVolumeLitres &&
        double.IsFinite(Throughput) && Throughput >= 0 && Stroke is >= 0 and <= 2 && Content != null &&
        float.IsFinite(RodOffsetX) && Math.Abs(RodOffsetX) <= Mechanics.ReciprocatingDriveMount.MaximumOffset + 1e-6 &&
        double.IsFinite(IntakeThroughput) && IntakeThroughput >= 0;

    internal PumpPresentation Copy() => (PumpPresentation)MemberwiseClone();
    internal bool SamePump(PumpPresentation other) => X == other.X && Y == other.Y &&
        Z == other.Z && Dimension == other.Dimension;
}

/// <summary>One shared, one-packet-delayed clock for the whole mechanical network.</summary>
internal sealed class PumpPresentationTimeline
{
    private PumpNetworkFrame? previous;
    private PumpNetworkFrame? latest;
    private long receivedAt;
    private double progress = 1;
    private bool stale;
    private long interval;
    public double Angle { get; private set; }
    public float Speed { get; private set; }
    public long LastReceivedAt => receivedAt;

    internal bool HasMatchingPump(Func<PumpPresentation, bool> matches) =>
        latest?.Pumps.Any(matches) == true;

    public bool Push(PumpNetworkFrame frame, long now)
    {
        if (!frame.IsValid || (latest != null && frame.Sequence <= latest.Sequence)) return false;
        long nextInterval = latest == null ? 0 : frame.ServerMilliseconds - latest.ServerMilliseconds;
        if (latest == null) previous = frame;
        else
        {
            // Receipt jitter must not teleport the water or piston to the
            // last packet's endpoint. Start at the state on screen right now.
            Advance(now);
            previous = new PumpNetworkFrame
            {
                Angle = Angle, Speed = Speed,
                Pumps = latest.Pumps.Select(p => TryPump(p.X, p.Y, p.Z, p.Dimension, out var shown) ? shown : p.Copy()).ToArray()
            };
        }
        interval = nextInterval;
        latest = frame;
        receivedAt = now;
        Advance(now);
        return true;
    }

    public void Advance(long now)
    {
        if (latest == null || previous == null) return;
        // Never extrapolate through an unknown stall. Moving and stopped
        // snapshots use the same continuous clock, including their contents.
        progress = interval <= 0 || interval > 500
            ? 1 : Math.Clamp((double)(now - receivedAt) / interval, 0, 1);
        stale = now - receivedAt > Math.Max(250, Math.Min(500, interval) * 2);
        Angle = previous.Angle + (latest.Angle - previous.Angle) * progress;
        Speed = stale ? 0 : progress >= 1 ? latest.Speed :
            (float)(previous.Speed + (latest.Speed - previous.Speed) * progress);
    }

    public bool TryPump(int x, int y, int z, int dimension, out PumpPresentation result)
    {
        result = null!;
        PumpPresentation? end = latest?.Pumps.FirstOrDefault(p => p.X == x && p.Y == y && p.Z == z && p.Dimension == dimension);
        if (end == null || latest == null || previous == null) return false;
        result = end.Copy();
        if (stale)
        {
            result.Throughput = 0;
            result.IntakeThroughput = 0;
            result.IntakeOpen = result.OutputOpen = false;
        }
        result.Angle = end.Angle + (Angle - latest.Angle) * end.Ratio;
        result.Volume = ReciprocatingPumpMath.ChamberVolumeLitres(result.Angle);
        // Air checks follow actual piston travel, not residual pressure that
        // may continue opening a wet check after the shaft has stopped.
        result.Stroke = Math.Abs(Speed) < .001 ? (int)ReciprocatingPumpStroke.Stationary :
            (int)ReciprocatingPumpMath.Stroke(result.Volume,
                ReciprocatingPumpMath.ChamberVolumeLitres(result.Angle + Math.Sign(Speed * end.Ratio) * .0001));
        result.IntakeOpen &= result.Stroke != (int)ReciprocatingPumpStroke.Pressure;
        result.OutputOpen &= result.Stroke != (int)ReciprocatingPumpStroke.Suction;
        PumpPresentation? start = previous.Pumps.FirstOrDefault(end.SamePump);
        if (start == null || progress >= 1 || start.Ratio != end.Ratio) return true;
        // Device angles come from atan2 and wrap at +/- pi. Reconstruct the
        // start from the continuous shaft clock, in the endpoint's angle frame,
        // so a small bottom-dead-centre crossing is not a nearly full turn.
        double startAngle = end.Angle + (previous.Angle - latest.Angle) * end.Ratio;
        // A content identity change is discrete. Keep the old contents until
        // this clock reaches the snapshot that changed them, never half-mix it.
        if (start.Content != end.Content)
        {
            result.Content = start.Content;
            result.Amount = start.Amount;
            result.Temperature = start.Temperature;
            result.Throughput = 0;
            result.IntakeThroughput = 0;
            result.IntakeOpen = result.OutputOpen = false;
            return true;
        }
        double fraction = progress;
        if (end.Amount > start.Amount && end.Content != HydraulicCodes.Steam)
        {
            // An interval can straddle top dead centre. Attribute intake to
            // its expansion half, so the remaining water cannot appear to
            // enter only after the piston has started moving down.
            double expansion = ReciprocatingPumpMath.ExpansionVolume(startAngle, end.Angle);
            if (expansion > 1e-9)
                fraction = Math.Clamp(ReciprocatingPumpMath.ExpansionVolume(startAngle, result.Angle) / expansion, 0, 1);
        }
        if (end.Amount < start.Amount && end.Content != HydraulicCodes.Steam)
        {
            // Empty piston travel cannot visually evacuate water. Use displaced
            // wet volume, not elapsed time, between two discharge snapshots.
            double contraction = ReciprocatingPumpMath.WetContractionVolume(startAngle, end.Angle, start.Amount);
            if (contraction > 1e-9)
                fraction = Math.Clamp(ReciprocatingPumpMath.WetContractionVolume(startAngle, result.Angle, start.Amount) / contraction, 0, 1);
        }
        result.Amount = start.Amount + (end.Amount - start.Amount) * fraction;
        result.Temperature = start.Temperature + (end.Temperature - start.Temperature) * progress;
        result.Throughput = start.Throughput + (end.Throughput - start.Throughput) * progress;
        result.IntakeThroughput = result.IntakeOpen
            ? start.IntakeThroughput + (end.IntakeThroughput - start.IntakeThroughput) * progress : 0;
        if (end.Amount < start.Amount)
        {
            result.IntakeOpen = false;
            result.OutputOpen &= fraction > 0;
        }
        else if (end.Amount > start.Amount)
        {
            result.OutputOpen = false;
            result.IntakeOpen &= fraction > 0;
        }
        return true;
    }
}
