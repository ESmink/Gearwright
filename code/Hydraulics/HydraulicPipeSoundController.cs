using System;
using Gearwright.Audio;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Client-only original water beds and rate-limited pressure information.</summary>
internal sealed class HydraulicPipeSoundController : IDisposable
{
    private const string PressureLimiterCacheKey = "gearwright:pressure-creak-limiter";
    private const float SustainedPressureSeconds = 5;
    private readonly BlockEntityFluidPipe pipe;
    private readonly ICoreClientAPI capi;
    private readonly LocalMachineLoop pipeFlow, nozzleFlow, sprinklerFlow, irrigatorFlow;
    private readonly PressureCreakLimiter pressureLimiter;
    private ILoadedSound? pressureCreakSound;
    private float highPressureSeconds;
    private bool disposed;

    public HydraulicPipeSoundController(BlockEntityFluidPipe pipe, ICoreClientAPI capi)
    {
        this.pipe = pipe; this.capi = capi;
        pipeFlow = new(capi, pipe.Pos, "water-pipe");
        nozzleFlow = new(capi, pipe.Pos, "water-outlet");
        sprinklerFlow = new(capi, pipe.Pos, "water-sprinkler");
        irrigatorFlow = new(capi, pipe.Pos, "water-irrigator");
        if (!capi.ObjectCache.TryGetValue(PressureLimiterCacheKey, out object? cached) || cached is not PressureCreakLimiter limiter)
        {
            limiter = new PressureCreakLimiter();
            capi.ObjectCache[PressureLimiterCacheKey] = limiter;
        }
        pressureLimiter = limiter;
    }

    public void Update(float seconds)
    {
        var entity = capi.World.Player?.Entity;
        double distance = entity != null && entity.Pos.Dimension == pipe.Pos.dimension
            ? Math.Sqrt(entity.CameraPos.SquareDistanceTo(pipe.Pos.ToVec3d().Add(.5, .5, .5)))
            : double.PositiveInfinity;
        UpdateAtDistance(seconds, distance);
    }

    internal void UpdateAtDistance(float seconds, double distance)
    {
        if (disposed) return;
        if (pressureCreakSound?.HasStopped == true) { pressureCreakSound.Dispose(); pressureCreakSound = null; }
        AssetLocation? content = pipe.CurrentContentCode;
        bool liquid = pipe.CanWriteState && content != null && !PipeContent.IsSteam(content) && pipe.ContentAmountLitres > 0;
        double strongest = 0;
        if (liquid)
            foreach (BlockFacing face in BlockFacing.ALLFACES)
                strongest = Math.Max(strongest, Math.Abs(pipe.GetNozzleFlowRate(face)));
        double spray = liquid && pipe is not BlockEntityIrrigatorPipe && pipe.HasSprinkler
            ? Math.Sqrt(HydraulicMath.Performance(pipe.CurrentPressure)) : 0;
        double trickle = liquid && pipe is BlockEntityIrrigatorPipe
            ? Math.Sqrt(HydraulicMath.IrrigatorPerformance(pipe.CurrentPressure)) : 0;
        // Irrigation carries its own flowing-water detail. Give the useful
        // outlet layer the voice instead of spending two slots per irrigator.
        sprinklerFlow.UpdateAtDistance(seconds, spray, 1, distance);
        irrigatorFlow.UpdateAtDistance(seconds, trickle, 1, distance);
        pipeFlow.UpdateAtDistance(seconds, liquid && spray <= 0 && trickle <= 0
            ? HydraulicMath.AudioFlowIntensity(pipe.ThroughputLitresPerSecond) : 0, 1, distance);
        nozzleFlow.UpdateAtDistance(seconds, liquid ? HydraulicMath.AudioFlowIntensity(strongest) : 0, 1, distance);
        UpdatePressureInformation(seconds, distance);
    }

    private void UpdatePressureInformation(float seconds, double distance)
    {
        float gain = MachineSoundPolicy.DistanceGain(distance, MachineSoundKind.Informational);
        float intensity = (float)HydraulicMath.PressureWarningIntensity(pipe.CurrentPressure);
        if (!pipe.CanWriteState || pipe.CurrentContentCode == null || pipe.ContentAmountLitres <= 0 ||
            pipe.CurrentPressure <= HydraulicMath.PressureWarningStartKPa || intensity <= 0 || gain <= 0 ||
            !float.IsFinite(seconds) || seconds <= 0 || seconds > .5f)
        {
            highPressureSeconds = 0;
            pressureCreakSound?.Stop(); pressureCreakSound?.Dispose(); pressureCreakSound = null;
            return;
        }
        highPressureSeconds = Math.Min(SustainedPressureSeconds, highPressureSeconds + seconds);
        if (pressureCreakSound != null)
        {
            pressureCreakSound.SetVolume(gain * (.03f + .13f * intensity));
            return;
        }
        // A pump stroke or transient pressure spike must not sound like a
        // continuously strained pipe after the readout has returned to normal.
        if (highPressureSeconds < SustainedPressureSeconds) return;
        if (!pressureLimiter.TryClaim(capi.World.ElapsedMilliseconds, intensity, capi.World.Rand)) return;
        var position = pipe.Pos.ToVec3f().Add(.5f, .5f, .5f);
        pressureCreakSound = capi.World.LoadSound(new SoundParams
        {
            Location = new("gearwright:sounds/machines/pressure-creak" + (1 + capi.World.Rand.Next(3)) + ".ogg"),
            Position = position, RelativePosition = false, ShouldLoop = false, DisposeOnFinish = false,
            Pitch = .97f + .06f * (float)capi.World.Rand.NextDouble(),
            Volume = gain * (.03f + .13f * intensity), ReferenceDistance = MachineSoundPolicy.InformationalReferenceDistance,
            Range = MachineSoundPolicy.InformationalRange, SoundType = EnumSoundType.Sound
        });
        if (pressureCreakSound == null)
        {
            capi.Logger.Error("[Gearwright] Pressure information sound is unavailable at {0}.", pipe.Pos);
            return;
        }
        pressureCreakSound.Start();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pipeFlow.Dispose(); nozzleFlow.Dispose(); sprinklerFlow.Dispose(); irrigatorFlow.Dispose();
        pressureCreakSound?.Stop(); pressureCreakSound?.Dispose(); pressureCreakSound = null;
    }

    private sealed class PressureCreakLimiter
    {
        private long nextCreakMilliseconds;
        public bool TryClaim(long now, float intensity, Random random)
        {
            if (now < nextCreakMilliseconds)
            {
                if (nextCreakMilliseconds - now < 120_000) return false;
                nextCreakMilliseconds = now;
            }
            float severity = Math.Clamp(intensity, 0, 1);
            double minimum = 60 - 40 * severity, maximum = 90 - 60 * severity;
            nextCreakMilliseconds = now + (long)((minimum + (maximum - minimum) * random.NextDouble()) * 1000);
            return true;
        }
    }
}
