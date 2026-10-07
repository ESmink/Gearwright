using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Audio;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class MechanicalAudioFixture
{
    internal static void Run(Action<bool, string> check)
    {
        check(Math.Abs(MachineMotionAudio.Travel(2 * Math.PI - .1, .1) - .2) < 1e-8 &&
            Math.Abs(MachineMotionAudio.Travel(.1, 2 * Math.PI - .1) + .2) < 1e-8,
            "Machine audio follows forward/reverse shaft wrap without a speed spike");
        check(MachineMotionAudio.Speed(.3, 1) == 0 && MachineMotionAudio.Gain(double.NaN) == 0 &&
            MachineMotionAudio.Speed(double.NaN, .1f) == 0,
            "Long frames and malformed motion rebase audio instead of replaying missed work");
        Contacts(check); Pump(check); Transmission(check);
        OverrunningTransmissionFixture.Run(check);
    }

    private static void Contacts(Action<bool, string> check)
    {
        var rig = new AudioRig();
        using (var contacts = new LocalMachineContacts(rig.Api, new BlockPos(0, 0, 0), "pump-valve"))
        {
            contacts.UpdateAtDistance(.1f, 2); contacts.Trigger();
            check(rig.Voices.Count == 0, "Recurring machine contacts never load at or beyond two blocks");
            for (int i = 0; i < 12; i++)
            {
                contacts.UpdateAtDistance(.1f, 0); contacts.Trigger();
            }
            check(rig.Voices.Count <= 4 && rig.Started.Count == 12 &&
                rig.Started.Zip(rig.Started.Skip(1)).All(pair => pair.First != pair.Second) &&
                rig.Voices.All(v => v.Parameters.Range == 2 && !v.Parameters.ShouldLoop &&
                    !v.Parameters.DisposeOnFinish && v.MaximumGain <= .2f),
                "One contact source reuses four soft variations and avoids adjacent repeats");
            int before = rig.Started.Count;
            contacts.Trigger();
            check(rig.Started.Count == before, "Contacts are rate limited without queueing a burst");
            contacts.UpdateAtDistance(.1f, 2.1);
            check(rig.Voices.All(v => v.Disposed && !v.Playing),
                "Leaving range stops and disposes every cached contact voice immediately");
        }
        int initial = rig.Voices.Count;
        var sources = Enumerable.Range(0, 9).Select(_ =>
            new LocalMachineContacts(rig.Api, new BlockPos(0, 0, 0), "ratchet-pawl")).ToArray();
        foreach (var source in sources) { source.UpdateAtDistance(.1f, 0); source.Trigger(); }
        check(rig.Voices.Count - initial == 8, "Dense machines retain at most eight nearby contact sources");
        sources[0].Dispose(); sources[^1].Trigger();
        check(rig.Voices.Count - initial == 9, "Disposal releases a contact source budget for waiting machinery");
        foreach (var source in sources) source.Dispose();
        check(rig.Voices.All(v => v.Disposed), "Unloaded machines release all loop and contact resources");
    }

    private static void Pump(Action<bool, string> check)
    {
        var rig = new AudioRig();
        using (var sound = new ReciprocatingPumpSoundController(rig.Api, new BlockPos(0, 0, 0)))
        {
            sound.UpdateAtDistance(.1f, 0, ReciprocatingPumpStroke.Suction, true, false, true, 0, 0);
            check(rig.Voices.Count == 0, "A first pump snapshot does not invent a startup stroke or valve impact");
            sound.UpdateAtDistance(.1f, .2, ReciprocatingPumpStroke.Suction, true, false, true, 4, 0);
            check(rig.Voices.Count == 2 && rig.Voices.Any(v => v.Cue == "pump-mechanism") &&
                rig.Voices.Any(v => v.Cue == "water-pipe"),
                "Wet pump motion uses its quiet mechanism and actual liquid throughput");
            sound.UpdateAtDistance(.1f, .4, ReciprocatingPumpStroke.Pressure, false, true, true, 4, 0);
            check(rig.Voices.Count(v => v.Cue.StartsWith("pump-valve")) == 1,
                "A wet valve closing and dry stroke reversal produce one combined rounded contact");
            int before = rig.Started.Count;
            for (int i = 0; i < 20; i++)
                sound.UpdateAtDistance(.1f, .4, ReciprocatingPumpStroke.Stationary, false, true, true, 0, 0);
            check(rig.Started.Count == before && rig.Voices.All(v => !v.Playing),
                "A stalled piston does not keep generating water, rubbing or repeated valve sounds");
            sound.UpdateAtDistance(.1f, .4, ReciprocatingPumpStroke.Stationary, false, true, true, 2, 0);
            check(rig.Voices.Any(v => v.Cue == "water-pipe" && v.Playing) &&
                rig.Voices.Where(v => v.Cue == "pump-mechanism").All(v => !v.Playing),
                "A stopped chamber can audibly discharge actual stored pressure without invented piston motion");
            sound.UpdateAtDistance(.1f, .4, ReciprocatingPumpStroke.Stationary, false, false, true, 2, 0);
            for (int i = 0; i < 20; i++)
                sound.UpdateAtDistance(.1f, .4, ReciprocatingPumpStroke.Stationary, false, false, true, 2, 0);
            check(rig.Voices.All(v => !v.Playing),
                "Closed displayed wet checks suppress residual throughput values");
            sound.UpdateAtDistance(.1f, .2, ReciprocatingPumpStroke.Suction, true, false, true, 4, 0);
            sound.UpdateAtDistance(.1f, .1, ReciprocatingPumpStroke.Suction, true, false, true, 4, 2);
            check(rig.Voices.All(v => v.Disposed), "Pump sound obeys the hard range cap after reverse motion");
        }
        rig = new AudioRig();
        using (var dry = new ReciprocatingPumpSoundController(rig.Api, new BlockPos(0, 0, 0)))
        {
            dry.UpdateAtDistance(.1f, 0, ReciprocatingPumpStroke.Suction, false, false, false, 0, 0);
            dry.UpdateAtDistance(.1f, .2, ReciprocatingPumpStroke.Pressure, false, false, false, 0, 0);
            check(rig.Voices.All(v => v.Cue != "water-pipe" && v.Cue != "airflow") &&
                rig.Voices.Any(v => v.Cue.StartsWith("pump-valve")),
                "Dry pumps retain dry-side check contacts without invented fluid flow");
        }
    }

    private static void Transmission(Action<bool, string> check)
    {
        var rig = new AudioRig();
        using var sound = new OverrunningTransmissionSoundController(rig.Api, new BlockPos(0, 0, 0));
        sound.UpdateAtDistance(.1f, new(0, .3f, 0, .3f), 1, true, 0);
        sound.UpdateAtDistance(.1f, new(0, .45f, 0, .3f), 1, true, 0);
        check(rig.Voices.Count(v => v.Cue.StartsWith("ratchet-pawl")) == 1 &&
            rig.Voices.All(v => v.Parameters.Range == 2),
            "Three aligned pawls produce one contact cluster per relative tooth crossing");
        int before = rig.Started.Count;
        sound.UpdateAtDistance(.1f, new(.1f, .55f, .2f, .2f), 1, true, 0);
        sound.UpdateAtDistance(.1f, new(.2f, .65f, .2f, .2f), 1, true, 0);
        check(rig.Started.Count == before, "Shafts co-rotating at equal speed produce no ratchet tooth clicks");
        sound.UpdateAtDistance(.1f, new(.4f, .8f, .4f, .3f), 1, false, 3);
        check(rig.Voices.Count(v => v.Cue.StartsWith("ratchet-engage")) == 1 &&
            rig.Voices.Last().Parameters.Range == 5 && rig.Voices.Last().MaximumGain < .17f,
            "A real engagement change gets a restrained five-block informational cue");
        int engagementStarts = rig.Started.Count(v => v.Cue.StartsWith("ratchet-engage"));
        sound.UpdateAtDistance(.1f, new(.45f, .85f, .4f, .3f), 1, true, 3);
        sound.UpdateAtDistance(.1f, new(.5f, .9f, .4f, .3f), 1, false, 3);
        check(rig.Started.Count(v => v.Cue.StartsWith("ratchet-engage")) == engagementStarts,
            "Rapid authoritative engagement changes cannot turn seating sounds into a repeated rattle");
        before = rig.Started.Count;
        sound.UpdateAtDistance(1, new(2, 3, .4f, .3f), 1, true, 0);
        check(rig.Started.Count == before, "A delayed renderer skips missed pawl contacts and state changes");
        sound.UpdateAtDistance(.1f, null, 1, true, 0);
        sound.UpdateAtDistance(.1f, new(0, 1, 0, .3f), -1, true, 0);
        check(rig.Started.Count == before, "Missing networks and direction changes rebase without phantom impacts");
        int pawlStarts = rig.Started.Count(v => v.Cue.StartsWith("ratchet-pawl"));
        sound.UpdateAtDistance(.1f, new(0, .8f, 0, -.3f), -1, true, 0);
        check(rig.Started.Count(v => v.Cue.StartsWith("ratchet-pawl")) == pawlStarts + 1,
            "Reverse freewheeling follows mirrored relative travel");
        sound.UpdateAtDistance(.1f, null, -1, true, 5);
        check(rig.Voices.All(v => v.Disposed), "Leaving informational range releases transmission voices");

        rig = new AudioRig();
        using var loaded = new OverrunningTransmissionSoundController(rig.Api, new BlockPos(0, 0, 0));
        var locked = OverrunningCouplingMath.Advance(default, .3f, .1f, 0, .05f);
        var recovering = OverrunningCouplingMath.Advance(locked.State, .2f, .25f,
            -OverrunningPawlMath.FullContactThreatPhaseLag, .05f);
        var packet = new OverrunningDriveState(true, recovering.State.Engaged, 1, 1, 2).Encode();
        OverrunningDriveState.TryDecode(packet, out var drive);
        loaded.UpdateAtDistance(.1f, new(0, .3f, .2f, .25f), drive.Handedness, !drive.Engaged, 0);
        loaded.UpdateAtDistance(.1f, new(.1f, .6f, .2f, .25f), drive.Handedness, !drive.Engaged, 0);
        check(rig.Started.TrueForAll(v => v.Cue == "transmission-bearing") &&
            rig.Voices.Exists(v => v.Cue == "transmission-bearing"),
            "Server-confirmed loaded phase recovery retains bearings without false freewheel clicks or engagement chatter");
    }

    private sealed class AudioRig
    {
        internal readonly List<Voice> Voices = new();
        internal readonly List<Voice> Started = new();
        internal readonly ICoreClientAPI Api;
        internal AudioRig()
        {
            var world = Stub.Create<IClientWorldAccessor>((method, args) =>
            {
                if (method.Name == "get_Rand") return random;
                if (method.Name != "LoadSound") return null;
                var voice = new Voice((SoundParams)args![0]!, Started); Voices.Add(voice);
                return voice.Sound;
            });
            Api = Stub.Create<ICoreClientAPI>((method, _) => method.Name == "get_World" ? world : null);
        }
        private readonly Random random = new(831);
    }

    private sealed class Voice
    {
        internal readonly SoundParams Parameters;
        internal readonly ILoadedSound Sound;
        internal readonly string Cue;
        internal bool Playing, Disposed;
        internal float MaximumGain;
        internal Voice(SoundParams parameters, List<Voice> started)
        {
            Parameters = parameters; Cue = parameters.Location.Path.Split('/').Last().Replace(".ogg", "");
            Sound = Stub.Create<ILoadedSound>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_SoundLengthSeconds": return parameters.ShouldLoop ? 240f : .4f;
                    case "Start": Playing = true; started.Add(this); break;
                    case "Stop": Playing = false; break;
                    case "Dispose": Disposed = true; Playing = false; break;
                    case "SetVolume": MaximumGain = Math.Max(MaximumGain, (float)args![0]!); break;
                }
                return null;
            });
        }
    }
}
