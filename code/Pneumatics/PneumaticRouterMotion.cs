using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

internal readonly record struct PneumaticRouterPose(double Angle, double Door, Vec3d Cargo);

internal static class PneumaticRouterMotion
{
    internal const double PipeSpeed = 2, CargoStop = 1.95 / 16;
    internal const double CatchEnd = (.5 + CargoStop) / PipeSpeed / PneumaticRouterState.TransitSeconds;
    internal const double CloseEnd = CatchEnd + .1 / PneumaticRouterState.TransitSeconds;
    internal const double LaunchStart = 1 - CatchEnd, OpenStart = LaunchStart - .1 / PneumaticRouterState.TransitSeconds;
    // A 1.5-second wave from fully dark to twice the usual brightness.
    internal static float AlertBrightness(double seconds) => (float)(1 - Math.Cos(seconds * Math.PI * 2 / 1.5));
    private static double S(double value) => PneumaticRenderer.Smooth(value);
    internal static double Turn(double from, double to) => ((to - from + 180) % 360 + 360) % 360 - 180;
    internal static PneumaticRouterPose Pose(BlockEntityPneumaticTransport host, double? presented = null)
    {
        var r = host.Router;
        if (host.State.Cargo == null)
        {
            double target = (r.ReadyInput - 1) * 90;
            double rest = ((r.RestAngle % 360) + 360) % 360, preparation = presented ?? r.Preparation;
            // Close, align, then open. A changed inlet cannot open the gate
            // while the carriage is still rotating toward its catch position.
            return new(rest + Turn(rest, target) * S((preparation - .25) / .5),
                r.ExpectedParcel != "" && host.Air > 0 ? S((preparation - .75) / .25) : 0, new(.5, .5, .5));
        }
        double p = Math.Clamp(presented ?? host.State.Progress, 0, 1), input = (r.TransitInput - 1) * 90, output = (r.TransitOutput - 1) * 90;
        if (r.Lost) p = Math.Min(p, CloseEnd);
        double angle = input + Turn(input, output) * S((p - CloseEnd) / (OpenStart - CloseEnd));
        double door = 1 - S((p - CatchEnd) / (CloseEnd - CatchEnd)) + S((p - OpenStart) / (LaunchStart - OpenStart));
        // These stretches are linear at the ordinary tube velocity, including
        // both shared faces. Only the stop/door/table work slows the router.
        return new(angle, door, Cargo(p, r.TransitInput, r.TransitOutput, r.Lost, angle));
    }
    internal static Vec3d Cargo(double p, int inlet, int outlet, bool lost, double mechanicalAngle)
    {
        p = Math.Clamp(p, 0, 1);
        if (lost) p = Math.Min(p, CloseEnd);
        double angle = p < CatchEnd ? (inlet - 1) * 90 : p >= LaunchStart ? (outlet - 1) * 90 : mechanicalAngle;
        double distance = p < CatchEnd ? -.5 + PipeSpeed * p * PneumaticRouterState.TransitSeconds :
            p < LaunchStart ? CargoStop : CargoStop - PipeSpeed * (p - LaunchStart) * PneumaticRouterState.TransitSeconds;
        double radians = angle * Math.PI / 180;
        return new(.5 + distance * Math.Cos(radians), .5, .5 - distance * Math.Sin(radians));
    }
    internal static AnimationKeyFrameElement? Bone(BlockEntityPneumaticTransport host, string name, double? presented = null, PneumaticRouterPose? mechanical = null)
    {
        var p = mechanical ?? Pose(host, presented); double lift = 5.22 * p.Door;
        var value = new AnimationKeyFrameElement();
        switch (name)
        {
            case "cassette": value.RotationY = p.Angle; break;
            case "index-iron": value.RotationZ = -p.Angle; break;
            case "index-temporal": value.RotationZ = p.Angle * 14 / 8; break;
            case "index-iron-shaft": value.RotationY = p.Angle; break;
            case "index-temporal-shaft": value.RotationY = -p.Angle * 14 / 8; break;
            case "gate-in": value.OffsetY = lift; break;
            case "door-in-iron": case "door-in-iron-shaft": value.RotationZ = -lift / 1.2 * 180 / Math.PI; break;
            case "door-in-temporal": case "door-in-temporal-shaft": value.RotationZ = lift / 1.2 * 180 / Math.PI * 6 / 8; break;
            default:
                for (int n = 1; n <= 4; n++)
                {
                    if (name == $"port-{n}-shutter")
                    {
                        value.OffsetY = Math.Abs(Turn(p.Angle, (n - 1) * 90)) < .001 ? Math.Max(0, lift - .02) : 0;
                        return value;
                    }
                    if (name == $"port-{n}-valve-pivot")
                    { value.RotationZ = host.PortRole(n) == "output" || n == host.Router.SupplyPort ? 90 : 0; return value; }
                    if (name.StartsWith($"port-{n}-scoop-", StringComparison.Ordinal))
                    { value.RotationZ = (host.PortRole(n) == "input" ? -38 : 38) - (n <= 2 ? -38 : 38); return value; }
                    if (name.StartsWith($"port-{n}-flow-chevron-", StringComparison.Ordinal))
                    {
                        int sign = name.Contains("chevron--", StringComparison.Ordinal) ? 1 : -1;
                        value.RotationX = sign * 40 * ((host.PortRole(n) == "input" ? -1 : 1) - (n <= 2 ? -1 : 1)); return value;
                    }
                }
                return null;
        }
        return value;
    }
}

/// <summary>Keep the rendered mechanism continuous across preparation, parcel
/// arrival, route repair and idle packets. Changing a parcel key never snaps it.</summary>
internal sealed class PneumaticRouterPresentation
{
    private PneumaticRouterPose from, target;
    private long started;
    private bool initialized;
    internal void Observe(PneumaticRouterPose pose, long now)
    {
        if (!initialized) { from = target = pose; started = now; initialized = true; return; }
        if (Math.Abs(PneumaticRouterMotion.Turn(target.Angle, pose.Angle)) < 1e-9 && Math.Abs(target.Door - pose.Door) < 1e-9) return;
        from = At(now);
        target = pose with { Angle = from.Angle + PneumaticRouterMotion.Turn(from.Angle, pose.Angle) };
        started = now;
    }
    internal PneumaticRouterPose At(long now)
    {
        double t = Math.Clamp((now - started) / 100.0, 0, 1);
        return new(from.Angle + (target.Angle - from.Angle) * t, from.Door + (target.Door - from.Door) * t,
            new Vec3d(.5, .5, .5));
    }
}
