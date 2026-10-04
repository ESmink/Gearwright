using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Audio;

/// <summary>One bounded contact voice per source, with four reusable original variations.</summary>
internal sealed class LocalMachineContacts : IDisposable
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, HashSet<LocalMachineContacts>> budgets = new();
    private readonly ICoreClientAPI api;
    private readonly BlockPos position;
    private readonly string cue;
    private readonly MachineSoundKind kind;
    private readonly float maximumVolume;
    private readonly HashSet<LocalMachineContacts> budget;
    private readonly ILoadedSound?[] variations = new ILoadedSound?[MachineSoundPolicy.WorkVariations];
    private ILoadedSound? playing;
    private int lastVariation = -1;
    private float idle, cooldown, distanceGain;
    private bool disposed, failed;

    internal LocalMachineContacts(ICoreClientAPI api, BlockPos position, string cue,
        MachineSoundKind kind = MachineSoundKind.Constant, float maximumVolume = .2f)
    {
        this.api = api; this.position = position.Copy(); this.cue = cue;
        this.kind = kind; this.maximumVolume = maximumVolume;
        budget = budgets.GetValue(api, _ => new());
    }

    internal void Update(float seconds) => UpdateAtDistance(seconds, MachineMotionAudio.Distance(api, position));

    internal void UpdateAtDistance(float seconds, double distance)
    {
        if (disposed) return;
        float elapsed = float.IsFinite(seconds) ? Math.Clamp(seconds, 0, .25f) : 0;
        idle += elapsed; cooldown = Math.Max(0, cooldown - elapsed);
        distanceGain = MachineSoundPolicy.DistanceGain(distance, kind);
        if (distanceGain <= 0 || idle >= 2) Release();
        else playing?.SetVolume(maximumVolume * distanceGain * lastIntensity);
    }

    private float lastIntensity;

    internal void Trigger(double intensity = 1, float pitch = 1)
    {
        float gain = MachineSoundPolicy.Intensity(intensity);
        if (disposed || failed || distanceGain <= 0 || gain <= 0 || cooldown > 0) return;
        // Skip excess contacts, rather than queueing a burst after a slow frame.
        if (!budget.Contains(this) && budget.Count >= 8) return;
        int index = api.World.Rand.Next(variations.Length - (lastVariation < 0 ? 0 : 1));
        if (lastVariation >= 0 && index >= lastVariation) index++;
        if (variations[index] == null)
        {
            string suffix = index == 0 ? "" : "-" + (index + 1);
            variations[index] = api.World.LoadSound(new SoundParams
            {
                Location = new("gearwright:sounds/machines/" + cue + suffix + ".ogg"),
                Position = position.ToVec3f().Add(.5f, .5f, .5f), RelativePosition = false,
                ShouldLoop = false, DisposeOnFinish = false, Volume = 0,
                Range = MachineSoundPolicy.Range(kind),
                ReferenceDistance = MachineSoundPolicy.Range(kind) - .1f, SoundType = EnumSoundType.Sound
            });
            if (variations[index] == null)
            {
                api.Logger.Error("[Gearwright] Machine contact sound {0} is missing; playback disabled.", cue);
                failed = true; Release(); return;
            }
        }
        playing?.Stop();
        playing = variations[index]; lastVariation = index; lastIntensity = gain;
        playing!.PlaybackPosition = 0;
        playing.SetPitch(float.IsFinite(pitch) ? Math.Clamp(pitch, .9f, 1.1f) : 1);
        playing.SetVolume(maximumVolume * distanceGain * gain);
        playing.Start(); budget.Add(this); idle = 0; cooldown = .045f;
    }

    private void Release()
    {
        playing = null;
        for (int i = 0; i < variations.Length; i++)
        {
            variations[i]?.Stop(); variations[i]?.Dispose(); variations[i] = null;
        }
        budget.Remove(this);
    }

    public void Dispose() { if (disposed) return; disposed = true; Release(); }
}
