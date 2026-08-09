using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Client-only, local ambient audio driven by replicated pipe state.</summary>
internal sealed class HydraulicPipeSoundController : IDisposable
{
    private const string PressureLimiterCacheKey = "gearwright:pressure-creak-limiter";
    private const float AmbientVolumeMultiplier = 2;
    private const float LoopReferenceDistanceBlocks = 0.9f;
    private const float PressureReferenceDistanceBlocks = 1;
    private static readonly AssetLocation[] PipeFlowLocations =
    {
        new("gearwright:sounds/hydraulics/water-dribble.ogg"),
        new("gearwright:sounds/hydraulics/water-hose.ogg")
    };
    private static readonly AssetLocation[] NozzleFlowLocations =
    {
        new("gearwright:sounds/hydraulics/water-hose.ogg"),
        new("gearwright:sounds/hydraulics/watering.ogg")
    };
    private static readonly AssetLocation[] SprinklerLocations =
    {
        new("gearwright:sounds/hydraulics/sprinkler.ogg"),
        new("gearwright:sounds/hydraulics/watering.ogg")
    };
    private static readonly AssetLocation[] IrrigatorLocations =
    {
        new("gearwright:sounds/hydraulics/watering.ogg")
    };
    private static readonly AssetLocation[] PressureCreakLocations =
    {
        new("gearwright:sounds/hydraulics/pressure-creak-heavy.ogg"),
        new("gearwright:sounds/hydraulics/pressure-creak-facility.ogg"),
        new("gearwright:sounds/hydraulics/pressure-creak.ogg")
    };

    private readonly BlockEntityFluidPipe pipe;
    private readonly ICoreClientAPI capi;
    private readonly Random random;
    private readonly Vec3f sourcePosition;
    private readonly PressureCreakLimiter pressureLimiter;
    private ILoadedSound? pipeFlowSound;
    private ILoadedSound? nozzleFlowSound;
    private ILoadedSound? sprinklerSound;
    private ILoadedSound? irrigatorSound;
    private ILoadedSound? pressureCreakSound;
    private float displayedPipeVolume;
    private float displayedNozzleVolume;
    private float displayedSprinklerVolume;
    private float displayedIrrigatorVolume;
    private float pipeQuietSeconds;
    private float nozzleQuietSeconds;
    private float sprinklerQuietSeconds;
    private float irrigatorQuietSeconds;
    private float pitchRefreshSeconds;
    private float pipePitchDrift;
    private float nozzlePitchDrift;
    private float sprinklerPitchDrift;
    private float irrigatorPitchDrift;
    private float volumeDrift = 1;
    private bool disposed;

    public HydraulicPipeSoundController(BlockEntityFluidPipe pipe, ICoreClientAPI capi)
    {
        this.pipe = pipe;
        this.capi = capi;
        random = capi.World.Rand;
        sourcePosition = pipe.Pos.ToVec3f().Add(0.5f, 0.5f, 0.5f);
        if (!capi.ObjectCache.TryGetValue(PressureLimiterCacheKey, out object? cached) ||
            cached is not PressureCreakLimiter limiter)
        {
            limiter = new PressureCreakLimiter();
            capi.ObjectCache[PressureLimiterCacheKey] = limiter;
        }
        pressureLimiter = limiter;
        RefreshDrift();
    }

    public void Update(float deltaTime)
    {
        if (disposed) return;
        CleanupStoppedCreak();
        pitchRefreshSeconds -= deltaTime;
        if (pitchRefreshSeconds <= 0) RefreshDrift();

        AssetLocation? content = pipe.CurrentContentCode;
        bool liquid = content != null && !PipeContent.IsSteam(content) && pipe.ContentAmountLitres > 0;
        float pipeIntensity = liquid
            ? (float)HydraulicMath.AudioFlowIntensity(pipe.ThroughputLitresPerSecond)
            : 0;
        float nozzleIntensity = liquid
            ? (float)HydraulicMath.AudioFlowIntensity(MaximumNozzleFlow())
            : 0;
        float sprinklerIntensity = liquid && pipe is not BlockEntityIrrigatorPipe && pipe.HasSprinkler
            ? (float)HydraulicMath.Performance(pipe.CurrentPressure)
            : 0;
        float irrigatorIntensity = liquid && pipe is BlockEntityIrrigatorPipe
            ? (float)HydraulicMath.IrrigatorPerformance(pipe.CurrentPressure)
            : 0;
        double listenerDistance = ListenerDistance();

        float pipeVolume = CanHear(pipeIntensity, listenerDistance)
            ? AmbientVolumeMultiplier * volumeDrift *
                (0.004f + 0.051f * MathF.Pow(pipeIntensity, 1.25f))
            : 0;
        float nozzleVolume = CanHear(nozzleIntensity, listenerDistance)
            ? AmbientVolumeMultiplier * volumeDrift *
                (0.006f + 0.084f * MathF.Pow(nozzleIntensity, 1.15f))
            : 0;
        float sprinklerVolume = CanHear(sprinklerIntensity, listenerDistance)
            ? AmbientVolumeMultiplier * volumeDrift *
                (0.004f + 0.051f * MathF.Sqrt(sprinklerIntensity))
            : 0;
        float irrigatorVolume = CanHear(irrigatorIntensity, listenerDistance)
            ? AmbientVolumeMultiplier * volumeDrift *
                (0.004f + 0.051f * MathF.Sqrt(irrigatorIntensity))
            : 0;

        UpdateLoop(
            ref pipeFlowSound, PipeFlowLocations, pipeVolume,
            0.88f + 0.18f * pipeIntensity + pipePitchDrift,
            deltaTime, ref displayedPipeVolume, ref pipeQuietSeconds);
        UpdateLoop(
            ref nozzleFlowSound, NozzleFlowLocations, nozzleVolume,
            0.92f + 0.15f * nozzleIntensity + nozzlePitchDrift,
            deltaTime, ref displayedNozzleVolume, ref nozzleQuietSeconds);
        UpdateLoop(
            ref sprinklerSound, SprinklerLocations, sprinklerVolume,
            0.86f + 0.20f * sprinklerIntensity + sprinklerPitchDrift,
            deltaTime, ref displayedSprinklerVolume, ref sprinklerQuietSeconds);
        UpdateLoop(
            ref irrigatorSound, IrrigatorLocations, irrigatorVolume,
            0.88f + 0.16f * irrigatorIntensity + irrigatorPitchDrift,
            deltaTime, ref displayedIrrigatorVolume, ref irrigatorQuietSeconds);
        UpdatePressureWarning(listenerDistance);
    }

    private void UpdatePressureWarning(double listenerDistance)
    {
        if (pipe.CurrentPressure < HydraulicMath.PressureWarningStartKPa) return;
        float intensity = (float)HydraulicMath.PressureWarningIntensity(pipe.CurrentPressure);
        float audibleIntensity = Math.Max(0.01f, intensity);
        if (!CanHear(audibleIntensity, listenerDistance) || pressureCreakSound != null) return;
        long now = capi.World.ElapsedMilliseconds;
        if (!pressureLimiter.TryClaim(now, intensity, random)) return;

        pressureCreakSound = capi.World.LoadSound(new SoundParams
        {
            Location = PressureCreakLocations[random.Next(PressureCreakLocations.Length)],
            Position = sourcePosition,
            RelativePosition = false,
            ShouldLoop = false,
            DisposeOnFinish = false,
            Pitch = Math.Clamp(0.82f + 0.25f * intensity + RandomSigned(0.08f), 0.72f, 1.18f),
            Volume = AmbientVolumeMultiplier * (0.022f + 0.143f * intensity) *
                RandomBetween(0.86f, 1.08f),
            ReferenceDistance = PressureReferenceDistanceBlocks,
            Range = (float)HydraulicMath.ExtremeAmbientAudioRangeBlocks,
            SoundType = EnumSoundType.Ambient
        });
        pressureCreakSound.Start();
    }

    private void UpdateLoop(
        ref ILoadedSound? sound,
        AssetLocation[] locations,
        float targetVolume,
        float pitch,
        float deltaTime,
        ref float displayedVolume,
        ref float quietSeconds)
    {
        float response = 1 - MathF.Exp(-Math.Min(deltaTime, 0.25f) * 3.5f);
        displayedVolume += (targetVolume - displayedVolume) * response;
        if (targetVolume > 0.001f)
        {
            quietSeconds = 0;
            sound ??= LoadLoop(locations);
            if (!sound.IsPlaying)
            {
                sound.Start();
                if (sound.SoundLengthSeconds > 1)
                    sound.PlaybackPosition = (float)random.NextDouble() * sound.SoundLengthSeconds;
            }
            sound.SetVolume(Math.Clamp(displayedVolume, 0, 1));
            sound.SetPitch(Math.Clamp(pitch, 0.72f, 1.22f));
            return;
        }

        quietSeconds += deltaTime;
        if (sound?.IsPlaying == true)
        {
            sound.SetVolume(Math.Clamp(displayedVolume, 0, 1));
            if (displayedVolume < 0.0005f) sound.Stop();
        }
        if (quietSeconds > 6 && sound != null)
        {
            DisposeSound(ref sound);
            displayedVolume = 0;
        }
    }

    private ILoadedSound LoadLoop(AssetLocation[] locations) => capi.World.LoadSound(new SoundParams
    {
        Location = locations[random.Next(locations.Length)],
        Position = sourcePosition,
        RelativePosition = false,
        ShouldLoop = true,
        DisposeOnFinish = false,
        Volume = 0,
        ReferenceDistance = LoopReferenceDistanceBlocks,
        Range = (float)HydraulicMath.ExtremeAmbientAudioRangeBlocks,
        SoundType = EnumSoundType.Ambient
    });

    private double MaximumNozzleFlow()
    {
        double strongest = 0;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
            strongest = Math.Max(strongest, Math.Abs(pipe.GetNozzleFlowRate(face)));
        return strongest;
    }

    private double ListenerDistance()
    {
        Vec3d listener = capi.World.Player.Entity.CameraPos;
        double dx = listener.X - sourcePosition.X;
        double dy = listener.Y - sourcePosition.Y;
        double dz = listener.Z - sourcePosition.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static bool CanHear(float intensity, double listenerDistance) =>
        intensity > 0 && listenerDistance <= HydraulicMath.AmbientAudioRange(intensity);

    private void RefreshDrift()
    {
        pitchRefreshSeconds = RandomBetween(1.8f, 4.2f);
        pipePitchDrift = RandomSigned(0.035f);
        nozzlePitchDrift = RandomSigned(0.03f);
        sprinklerPitchDrift = RandomSigned(0.025f);
        irrigatorPitchDrift = RandomSigned(0.025f);
        volumeDrift = RandomBetween(0.88f, 1.08f);
    }

    private void CleanupStoppedCreak()
    {
        if (pressureCreakSound?.HasStopped != true) return;
        DisposeSound(ref pressureCreakSound);
    }

    private float RandomBetween(float minimum, float maximum) =>
        minimum + (maximum - minimum) * (float)random.NextDouble();

    private float RandomSigned(float magnitude) =>
        ((float)random.NextDouble() * 2 - 1) * magnitude;

    private static void DisposeSound(ref ILoadedSound? sound)
    {
        if (sound == null) return;
        if (sound.IsPlaying) sound.Stop();
        sound.Dispose();
        sound = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DisposeSound(ref pipeFlowSound);
        DisposeSound(ref nozzleFlowSound);
        DisposeSound(ref sprinklerSound);
        DisposeSound(ref irrigatorSound);
        DisposeSound(ref pressureCreakSound);
    }

    private sealed class PressureCreakLimiter
    {
        private long nextCreakMilliseconds;

        public bool TryClaim(long now, float intensity, Random random)
        {
            if (now < nextCreakMilliseconds)
            {
                // A new world can reset elapsed time while retaining the client object cache.
                if (nextCreakMilliseconds - now < 120_000) return false;
                nextCreakMilliseconds = now;
            }
            float severity = Math.Clamp(intensity, 0, 1);
            double minimumSeconds = 30 - 25 * severity;
            double maximumSeconds = 48 - 40 * severity;
            double delay = minimumSeconds +
                (maximumSeconds - minimumSeconds) * random.NextDouble();
            nextCreakMilliseconds = now + (long)(delay * 1000);
            return true;
        }
    }
}
