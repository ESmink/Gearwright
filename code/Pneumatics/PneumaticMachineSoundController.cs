using System;
using System.Collections.Generic;
using Gearwright.Audio;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

internal readonly record struct PneumaticWorkPhase(string Cue, string Cycle, double Progress, float Seconds, float Gain = 1);

/// <summary>Audible mechanism phases follow replicated progress, including stalls and empty return.</summary>
internal sealed class PneumaticMachineSoundController : IDisposable
{
    private readonly BlockEntityPneumaticTransport host;
    private readonly ICoreClientAPI api;
    private readonly HashSet<string> missing = new();
    private readonly Dictionary<string, int> lastVariations = new();
    private string selectedCue = "";
    private PneumaticWorkPhase? previous;
    private ILoadedSound? sound;
    private bool playing, disposed;
    private float quietSeconds;

    internal PneumaticMachineSoundController(BlockEntityPneumaticTransport host, ICoreClientAPI api)
    { this.host = host; this.api = api; }

    internal static PneumaticWorkPhase? Describe(BlockEntityPneumaticTransport host)
    {
        if (!host.CanWrite) return null;
        var s = host.State;
        if (s.Loading && s.Cargo != null || s.Returning)
            return new("sender-work", "sender", s.Returning ? s.ReturnProgress : s.Progress, 1.6f);
        if (host.Kind == PneumaticLineKind.Router)
        {
            var r = host.Router;
            if (s.Cargo != null)
                return new("router-turn", s.Parcel, r.Lost ? Math.Min(s.Progress, PneumaticRouterMotion.CloseEnd) : s.Progress,
                    (float)PneumaticRouterState.TransitSeconds);
            if (r.ExpectedParcel != "")
            {
                bool turn = Math.Abs(PneumaticRouterMotion.Turn(r.RestAngle, (r.ReadyInput - 1) * 90)) > .001;
                float gain = !turn && r.Preparation >= .25 && r.Preparation <= .75 ? 0 : 1;
                return new("router-prepare", "prepare:" + r.ExpectedParcel + ":" + r.ReadyInput,
                    r.Preparation, (float)PneumaticRouterState.PrepareSeconds, gain);
            }
        }
        if (s.Cargo != null && host.IsReceiving)
            return new("receiver-work", s.Parcel, s.Progress, 1.6f);
        return null;
    }

    internal void Update(float seconds)
    {
        var entity = api.World.Player?.Entity;
        double distance = entity != null && entity.Pos.Dimension == host.Pos.dimension
            ? Math.Sqrt(entity.CameraPos.SquareDistanceTo(host.Pos.ToVec3d().Add(.5, .5, .5))) : double.PositiveInfinity;
        UpdateAtDistance(seconds, Describe(host), distance);
    }

    internal void UpdateAtDistance(float seconds, PneumaticWorkPhase? phase, double distance)
    {
        if (disposed) return;
        if (phase is not { } current || !double.IsFinite(current.Progress) || current.Progress < 0 ||
            current.Progress > 1 || !float.IsFinite(current.Seconds) || current.Seconds <= 0)
        { Release(); previous = null; return; }
        bool sameCycle = previous is { } old && old.Cycle == current.Cycle && old.Cue == current.Cue &&
            current.Progress >= old.Progress;
        double from = sameCycle ? previous!.Value.Progress : current.Progress;
        if (!sameCycle)
        {
            Release();
            int variation = api.World.Rand.Next(1, MachineSoundPolicy.WorkVariations + 1);
            if (lastVariations.TryGetValue(current.Cue, out int last) && variation == last)
                variation = variation % MachineSoundPolicy.WorkVariations + 1;
            lastVariations[current.Cue] = variation;
            selectedCue = variation == 1 ? current.Cue : current.Cue + "-" + variation;
        }
        previous = current;
        float gain = MachineSoundPolicy.DistanceGain(distance, MachineSoundKind.Informational);
        if (gain <= 0) { Release(); return; }
        float elapsed = float.IsFinite(seconds) ? Math.Clamp(seconds, .001f, .25f) : .1f;
        if (current.Progress - from <= 1e-8)
        {
            // Do not play ahead of a parked tray, blocked gate or lost route.
            if (playing) { sound?.SetVolume(0); sound?.Pause(); playing = false; }
            quietSeconds += elapsed;
            if (quietSeconds >= 2) Release();
            return;
        }
        quietSeconds = 0;
        if (sound == null)
        {
            if (missing.Contains(current.Cue)) return;
            sound = api.World.LoadSound(new SoundParams
            {
                Location = new("gearwright:sounds/machines/" + selectedCue + ".ogg"),
                Position = host.Pos.ToVec3f().Add(.5f, .5f, .5f), RelativePosition = false,
                ShouldLoop = false, DisposeOnFinish = false, Volume = 0,
                Range = MachineSoundPolicy.InformationalRange,
                ReferenceDistance = MachineSoundPolicy.InformationalReferenceDistance, SoundType = EnumSoundType.Sound
            });
            if (sound == null || Math.Abs(sound.SoundLengthSeconds - current.Seconds) > .05)
            {
                api.Logger.Error("[Gearwright] Work sound {0} is missing or does not match its mechanism duration; playback disabled.", current.Cue);
                missing.Add(current.Cue); Release(); return;
            }
        }
        float offset = (float)(from * current.Seconds);
        if (!playing || sound.HasStopped)
        { sound.Start(); sound.PlaybackPosition = offset; playing = true; }
        else if (Math.Abs(sound.PlaybackPosition - offset) > .08f)
            sound.PlaybackPosition = offset;
        sound.SetPitch(Math.Clamp((float)((current.Progress - from) * current.Seconds / elapsed), .25f, 1.25f));
        sound.SetVolume(MachineSoundPolicy.InformationalVolume * gain * MachineSoundPolicy.Intensity(current.Gain));
    }

    private void Release()
    {
        if (sound != null) { sound.Stop(); sound.Dispose(); sound = null; }
        playing = false; quietSeconds = 0;
    }

    public void Dispose()
    { if (disposed) return; disposed = true; Release(); }
}
