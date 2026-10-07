using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Audio;

// Presentation categories only; never serialized or used to drive simulation.
internal enum MachineSoundKind { Constant, Informational, Warning }

internal static class MachineSoundPolicy
{
    internal const float ConstantRange = 2, InformationalRange = 5, WarningRange = 20;
    internal const float ConstantVolume = .5f, InformationalVolume = .34f, WarningVolume = 1;
    internal const float AirflowVolume = .4f;
    internal const float IrrigationRange = 5;
    internal const int WorkVariations = 4;
    // Leave a small valid attenuation interval for engine distance models.
    internal const float ConstantReferenceDistance = ConstantRange - .1f;
    internal const float InformationalReferenceDistance = InformationalRange - .1f;
    internal const float MinimumConstantSeconds = 180;
    internal const int MaximumNearbyLoops = 8;

    internal static float Range(MachineSoundKind kind) => kind switch
    {
        MachineSoundKind.Constant => ConstantRange,
        MachineSoundKind.Informational => InformationalRange,
        MachineSoundKind.Warning => WarningRange,
        _ => 0
    };

    internal static float DistanceGain(double distance, MachineSoundKind kind)
    {
        double near = kind == MachineSoundKind.Constant ? 1.25 : 1;
        return DistanceFade(distance, Range(kind), near);
    }

    internal static float ContinuousRange(string cue) =>
        cue is "water-sprinkler" or "water-irrigator" ? IrrigationRange : ConstantRange;

    internal static float ContinuousDistanceGain(double distance, string cue) =>
        DistanceFade(distance, ContinuousRange(cue),
            cue is "water-sprinkler" or "water-irrigator" ? 2.5 : 1.25);

    private static float DistanceFade(double distance, double range, double near)
    {
        if (!double.IsFinite(distance) || distance < 0 || range <= 0 || distance >= range) return 0;
        // Keep machinery audible at normal head height beside a floor-mounted
        // block. The engine uses a flat near field; this is the only fade.
        double t = Math.Clamp((distance - near) / (range - near), 0, 1);
        return (float)(1 - t * t * (3 - 2 * t));
    }

    internal static float Intensity(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0;

    internal static float ContinuousVolume(string cue) => cue switch
    {
        "airflow" => AirflowVolume,
        "flywheel" => .32f,
        "pump-mechanism" => .36f,
        "transmission-bearing" => .16f,
        _ => ConstantVolume
    };

    internal static void Information(IWorldAccessor world, BlockPos position, string cue)
    {
        if (world.Side != EnumAppSide.Server) return;
        world.PlaySoundAt(new AssetLocation("gearwright:sounds/machines/" + cue + ".ogg"),
            position, 0, null, randomizePitch: false, range: InformationalRange, volume: InformationalVolume);
    }
}
