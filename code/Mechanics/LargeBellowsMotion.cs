using System;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

internal readonly record struct BellowsPoint(double X, double Y)
{
    public static BellowsPoint operator +(BellowsPoint a, BellowsPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static BellowsPoint operator -(BellowsPoint a, BellowsPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static BellowsPoint operator *(BellowsPoint a, double b) => new(a.X * b, a.Y * b);
    public double Length => Math.Sqrt(X * X + Y * Y);
}

internal readonly record struct BellowsPose(BellowsPoint Journal, BellowsPoint Input,
    BellowsPoint Output, BellowsPoint Plate, double RockerAngle, double BottomAngle, double UpperFill);

/// <summary>Constant-length links; angles are radians and distances are model units.</summary>
internal static class LargeBellowsMotion
{
    public const double TopLinkLength = 14.700298516216503;
    public static readonly double AirScale = .2 / (Math.Sin(11.816307918486983 * Math.PI / 180) -
        Math.Sin(4.005752574621118 * Math.PI / 180));
    public static BellowsPoint Rotate(BellowsPoint p, double angle) =>
        new(p.X * Math.Cos(angle) - p.Y * Math.Sin(angle), p.X * Math.Sin(angle) + p.Y * Math.Cos(angle));

    private static (BellowsPoint Point, double Angle) Joint(BellowsPoint hinge,
        BellowsPoint anchor, BellowsPoint driver, double length)
    {
        BellowsPoint delta = driver - hinge;
        double r = anchor.Length, d = delta.Length;
        double angle = Math.Atan2(delta.Y, delta.X) + Math.Acos(Math.Clamp(
            (r * r + d * d - length * length) / (2 * r * d), -1, 1)) - Math.Atan2(anchor.Y, anchor.X);
        return (hinge + Rotate(anchor, angle), angle);
    }

    public static BellowsPoint UpperPin(double fill) => new BellowsPoint(4, 10.8) +
        Rotate(new(0, .4), 1.5 * fill * GameMath.DEG2RAD) +
        Rotate(new(0, .6), 9.5 * fill * GameMath.DEG2RAD) +
        Rotate(new(14.8095, 2.5), 11.5 * fill * GameMath.DEG2RAD);

    public static BellowsPose Pose(double phase, bool top)
    {
        if (!double.IsFinite(phase)) phase = 0;
        double shift = top ? 32 : 0;
        BellowsPoint journal = new(24 + 3 * Math.Sin(phase), -8 + shift + 3 * Math.Cos(phase));
        BellowsPoint rocker = new(16.8, -2 + shift);
        var input = Joint(rocker, new(7.2, 0), journal, 7);
        BellowsPoint output = rocker + (input.Point - rocker) * (top ? 3.0 / 7.2 : .6);
        if (!top)
        {
            var plate = Joint(new(3.9095, 8.9055), new(20.9, -2), output, 6.6);
            return new(journal, input.Point, output, plate.Point, input.Angle, plate.Angle, 0);
        }
        double low = 0, high = 1;
        for (int i = 0; i < 40; i++)
        {
            double middle = (low + high) / 2;
            if ((UpperPin(middle) - output).Length > TopLinkLength) low = middle;
            else high = middle;
        }
        double fill = (low + high) / 2;
        // Suction into the lifted upper chamber draws the lower board upward.
        // As the top returns, the lower board drops and its inlet opens to refill.
        // This passive follower adds no second mechanical connection or air stroke.
        double bottomAngle = (-12 + 8 * fill) * GameMath.DEG2RAD;
        return new(journal, input.Point, output, UpperPin(fill), input.Angle, bottomAngle, fill);
    }

    public static double Volume(double phase, bool top)
    {
        BellowsPose pose = Pose(phase, top);
        return top ? Math.Sin(11.5 * pose.UpperFill * GameMath.DEG2RAD) : -Math.Sin(pose.BottomAngle);
    }

    public static bool IsTakingInAir(double phase, double travel, bool top) =>
        double.IsFinite(travel) && Math.Abs(travel) > 1e-10 &&
        Pose(phase + travel, top).BottomAngle < Pose(phase, top).BottomAngle;

    public static double PumpedAir(double from, double to, bool top)
    {
        if (!double.IsFinite(from) || !double.IsFinite(to)) return 0;
        // The timing system normally samples <= .05 radians. Subdivision also
        // keeps reversals and a compression extremum conservative in tests.
        double travel = Math.Clamp(to - from, -Math.PI * 2, Math.PI * 2);
        int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(travel) / .025), 1, 256);
        double previous = Volume(from, top), air = 0;
        for (int i = 1; i <= steps; i++)
        {
            double next = Volume(from + travel * i / steps, top);
            air += Math.Max(0, previous - next) * AirScale;
            previous = next;
        }
        return air;
    }

    public static double Phase(double deviceAngle, bool top) => (top ? 0 : Math.PI) - deviceAngle;
}
