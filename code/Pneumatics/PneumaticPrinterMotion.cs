using System;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

/// <summary>Independent print/feed presentation. Parcel movement keeps its own cycle.</summary>
internal static class PneumaticPrinterMotion
{
    private static readonly (double Frame, double Value)[] TravelKeys = { (0, 0), (20, 0), (24, .65), (32, .65), (36, 1.3), (44, 1.3), (48, 1.95), (104, 1.95), (120, 0) };
    private static double Curve(double frame, (double Frame, double Value)[] keys)
    {
        for (int i = 1; i < keys.Length; i++) if (frame <= keys[i].Frame)
        {
            var a = keys[i - 1]; var b = keys[i];
            return a.Value + (b.Value - a.Value) * PneumaticRenderer.Smooth((frame - a.Frame) / (b.Frame - a.Frame));
        }
        return keys[^1].Value;
    }
    internal static (double Travel, double Press, double Feed) Pose(double progress)
    {
        double frame = Math.Clamp(progress, 0, 1) * 120;
        double travel = Curve(frame, TravelKeys);
        double press = 0;
        for (int hit = 18; hit <= 54; hit += 12) press += .42 * PneumaticRenderer.Smooth((frame - hit + 6) / 4) * (1 - PneumaticRenderer.Smooth((frame - hit) / 2));
        double feed = PneumaticStockkeeperState.FeedDistance * PneumaticRenderer.Smooth((frame - 60) / 40);
        return (travel, press, feed);
    }
    internal static AnimationKeyFrameElement? Bone(string name, PneumaticStockkeeperState state, double presented)
    {
        if (!name.StartsWith("stock-", StringComparison.Ordinal)) return null;
        var motion = state.Printing ? Pose(presented) : (Travel: 0d, Press: 0d, Feed: 0d);
        if (name == "stock-print-carriage") return new() { OffsetZ = motion.Travel };
        if (name == "stock-print-plunger") return new() { OffsetZ = motion.Travel, OffsetY = -motion.Press };
        if (name is "stock-supply-roll" or "stock-takeup-roll" or "stock-feed-wheel")
            return new() { RotationZ = -state.PaperAngle - motion.Feed / PneumaticStockkeeperState.RollRadius * 180 / Math.PI };
        if (name.StartsWith("stock-print-ink-", StringComparison.Ordinal) && int.TryParse(name.AsSpan(16), out int stamp))
            return new() { OffsetX = motion.Feed, OffsetY = state.Printing && presented * 120 >= 18 + stamp * 12 ? .18 : 0 };
        if (name.StartsWith("stock-print-history-", StringComparison.Ordinal) && int.TryParse(name.AsSpan(20), out int age))
        {
            bool visible = age < state.PrintedRows && 5.25 + PneumaticStockkeeperState.FeedDistance * (age + 1) + motion.Feed < 11.72;
            return new() { OffsetX = motion.Feed, OffsetY = visible ? .18 : 0 };
        }
        return null;
    }
}
