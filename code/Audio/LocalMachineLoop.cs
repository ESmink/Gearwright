using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Audio;

/// <summary>Bounded client playback; distant sources never decode a long loop.</summary>
internal sealed class LocalMachineLoop : IDisposable
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, HashSet<LocalMachineLoop>> budgets = new();
    private readonly ICoreClientAPI api;
    private readonly BlockPos position;
    private readonly AssetLocation location;
    private readonly string cue;
    private readonly HashSet<LocalMachineLoop> budget;
    private readonly float maximumVolume;
    private ILoadedSound? sound;
    private float volume, quietSeconds;
    private bool disposed, failed;

    internal LocalMachineLoop(ICoreClientAPI api, BlockPos position, string name)
    {
        this.api = api; this.position = position.Copy();
        cue = name;
        location = new("gearwright:sounds/machines/" + name + ".ogg");
        maximumVolume = MachineSoundPolicy.ContinuousVolume(name);
        budget = budgets.GetValue(api, _ => new());
    }

    internal void Update(float seconds, double intensity, float pitch = 1)
    {
        var entity = api.World.Player?.Entity;
        double distance = double.PositiveInfinity;
        if (entity != null && entity.Pos.Dimension == position.dimension)
        {
            var center = position.ToVec3d().Add(.5, .5, .5);
            distance = Math.Sqrt(entity.CameraPos.SquareDistanceTo(center));
        }
        UpdateAtDistance(seconds, intensity, pitch, distance);
    }

    internal void UpdateAtDistance(float seconds, double intensity, float pitch, double distance)
    {
        if (disposed || failed) return;
        float elapsed = float.IsFinite(seconds) ? Math.Clamp(seconds, 0, .25f) : 0;
        float distanceGain = MachineSoundPolicy.ContinuousDistanceGain(distance, cue);
        if (distanceGain <= 0)
        {
            // Stop immediately outside this source's range, even during a fade.
            Release(); volume = quietSeconds = 0; return;
        }
        float target = maximumVolume * MachineSoundPolicy.Intensity(intensity) * distanceGain;
        volume += (target - volume) * (1 - MathF.Exp(-elapsed * 5));
        if (target > .0001f)
        {
            quietSeconds = 0;
            if (sound == null)
            {
                if (budget.Count >= MachineSoundPolicy.MaximumNearbyLoops) { volume = 0; return; }
                sound = api.World.LoadSound(new SoundParams
                {
                    Location = location, Position = position.ToVec3f().Add(.5f, .5f, .5f),
                    RelativePosition = false, ShouldLoop = true, DisposeOnFinish = false,
                    Volume = 0, Range = MachineSoundPolicy.ContinuousRange(cue),
                    ReferenceDistance = MachineSoundPolicy.ContinuousRange(cue) - .1f, SoundType = EnumSoundType.Sound
                });
                if (sound == null || sound.SoundLengthSeconds < MachineSoundPolicy.MinimumConstantSeconds)
                {
                    api.Logger.Error("[Gearwright] Constant sound {0} is missing or shorter than three minutes; playback disabled.", location);
                    sound?.Dispose(); sound = null; failed = true; return;
                }
                budget.Add(this);
                sound.Start();
                sound.PlaybackPosition = (float)(api.World.Rand.NextDouble() * sound.SoundLengthSeconds);
            }
            sound.SetVolume(Math.Clamp(volume, 0, maximumVolume));
            sound.SetPitch(float.IsFinite(pitch) ? Math.Clamp(pitch, .95f, 1.05f) : 1);
        }
        else
        {
            quietSeconds += elapsed;
            sound?.SetVolume(Math.Max(0, volume));
            if (quietSeconds >= 1.5f) { Release(); volume = 0; }
        }
    }

    private void Release()
    {
        if (sound != null) { sound.Stop(); sound.Dispose(); sound = null; }
        budget.Remove(this);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Release();
    }
}
