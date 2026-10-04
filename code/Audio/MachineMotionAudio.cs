using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Audio;

internal static class MachineMotionAudio
{
    internal static double Distance(ICoreClientAPI api, BlockPos position)
    {
        var entity = api.World.Player?.Entity;
        return entity != null && entity.Pos.Dimension == position.dimension
            ? Math.Sqrt(entity.CameraPos.SquareDistanceTo(position.ToVec3d().Add(.5, .5, .5)))
            : double.PositiveInfinity;
    }

    internal static double Travel(double previous, double current) =>
        double.IsFinite(previous) && double.IsFinite(current)
            ? Math.IEEERemainder(current - previous, 2 * Math.PI) : 0;

    internal static double Speed(double travel, float seconds) =>
        float.IsFinite(seconds) && seconds > 0 && seconds <= .25f && double.IsFinite(travel)
            ? Math.Abs(travel / seconds) : 0;

    internal static double Gain(double radiansPerSecond) =>
        double.IsFinite(radiansPerSecond) && radiansPerSecond > .005
            ? Math.Clamp(Math.Sqrt(radiansPerSecond / (2 * Math.PI)), 0, 1) : 0;

    internal static bool CrossedTooth(double previous, double travel, double pitch) =>
        double.IsFinite(previous) && double.IsFinite(travel) && travel > 0 &&
        Math.Floor(previous / pitch) < Math.Floor((previous + travel) / pitch);
}
