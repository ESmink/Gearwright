using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Audio;
using Gearwright.Pneumatics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class MachineAudioFixture
{
    internal static void Run(Action<bool, string> check)
    {
        check(MachineSoundPolicy.Range(MachineSoundKind.Constant) == 2 &&
            MachineSoundPolicy.Range(MachineSoundKind.Informational) == 5 &&
            MachineSoundPolicy.Range(MachineSoundKind.Warning) == 20,
            "Sound categories retain the maintainer's 2/5/20-block limits");
        foreach (MachineSoundKind kind in Enum.GetValues<MachineSoundKind>())
        {
            double range = MachineSoundPolicy.Range(kind);
            check(MachineSoundPolicy.DistanceGain(0, kind) == 1 &&
                MachineSoundPolicy.DistanceGain(range - .1, kind) > 0 &&
                MachineSoundPolicy.DistanceGain(range, kind) == 0 &&
                MachineSoundPolicy.DistanceGain(range + .1, kind) == 0 &&
                MachineSoundPolicy.DistanceGain(double.NaN, kind) == 0,
                "Sound fades at its category boundary and rejects invalid distances: " + kind);
        }
        check(MachineSoundPolicy.DistanceGain(1.25, MachineSoundKind.Constant) == 1 &&
            MachineSoundPolicy.DistanceGain(1.6, MachineSoundKind.Constant) > .5f,
            "Continuous sound retains useful gain at normal head height beside a machine");
        check(MachineSoundPolicy.InformationalVolume < .5f,
            "Informational playback is quieter than the rejected first pass");
        check(MachineSoundPolicy.ContinuousVolume("airflow") < MachineSoundPolicy.ContinuousVolume("bellows"),
            "Airflow uses a lower gain than the other continuous machinery after listening feedback");
        EnumAppSide side = EnumAppSide.Client;
        List<float> eventRanges = new();
        List<AssetLocation> eventLocations = new();
        var eventWorld = Stub.Create<IWorldAccessor>((method, args) =>
        {
            if (method.Name == "get_Side") return side;
            if (method.Name == "PlaySoundAt")
            {
                int range = Array.FindIndex(method.GetParameters(), parameter => parameter.Name == "range");
                eventRanges.Add((float)args![range]!);
                eventLocations.Add((AssetLocation)args[0]!);
            }
            return null;
        });
        MachineSoundPolicy.Information(eventWorld, new BlockPos(0, 0, 0), "sender-work");
        side = EnumAppSide.Server;
        MachineSoundPolicy.Information(eventWorld, new BlockPos(0, 0, 0), "receiver-work");
        check(eventRanges.SequenceEqual(new[] { 5f }) && eventLocations.Single().ToString() ==
            "gearwright:sounds/machines/receiver-work.ogg",
            "Clients cannot broadcast work cues; authoritative events use the SDK's five-block sound range");
        List<SoundParams> requests = new();
        List<float> gains = new(), offsets = new();
        int starts = 0, stops = 0, disposals = 0, errors = 0;
        float duration = 240;
        var logger = Stub.Create<ILogger>((method, _) => { if (method.Name == "Error") errors++; return null; });
        var random = new Random(812);
        var world = Stub.Create<IClientWorldAccessor>((method, args) =>
        {
            if (method.Name == "get_Rand") return random;
            if (method.Name == "LoadSound")
            {
                requests.Add((SoundParams)args![0]!);
                float capturedDuration = duration;
                return Stub.Create<ILoadedSound>((soundMethod, soundArgs) =>
                {
                    switch (soundMethod.Name)
                    {
                        case "get_SoundLengthSeconds": return capturedDuration;
                        case "Start": starts++; break;
                        case "Stop": stops++; break;
                        case "Dispose": disposals++; break;
                        case "SetVolume": gains.Add((float)soundArgs![0]!); break;
                        case "set_PlaybackPosition": offsets.Add((float)soundArgs![0]!); break;
                    }
                    return null;
                });
            }
            return null;
        });
        var api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Logger" => logger, _ => null });
        using (var loop = new LocalMachineLoop(api, new BlockPos(0, 0, 0), "airflow"))
        {
            loop.UpdateAtDistance(.1f, 1, 1, 2.01);
            check(requests.Count == 0, "Distant machines do not decode a four-minute sound");
            loop.UpdateAtDistance(.1f, 1, 1, .4);
            loop.UpdateAtDistance(.1f, 1, 1, .4);
            check(starts == 1 && requests.Count == 1 && requests[0].Range == 2 &&
                requests[0].ReferenceDistance == MachineSoundPolicy.ConstantReferenceDistance &&
                requests[0].ReferenceDistance < requests[0].Range && requests[0].SoundType == EnumSoundType.Sound &&
                requests[0].ShouldLoop && !requests[0].DisposeOnFinish &&
                gains.All(g => g >= 0 && g <= MachineSoundPolicy.AirflowVolume) &&
                offsets.Single() > 0 && offsets.Single() < 240,
                "Nearby loops start once, use quiet capped gain, and begin at a random point in long content");
            for (int tick = 0; tick < 20; tick++) loop.UpdateAtDistance(.1f, 1, 1, 1.2);
            check(gains[^1] > MachineSoundPolicy.AirflowVolume * .95f,
                "Close continuous playback retains its full chosen gain without stacked engine attenuation");
            loop.UpdateAtDistance(.1f, 1, 1, 2.01);
            check(stops == 1 && disposals == 1, "Leaving two blocks immediately stops and disposes constant audio");
            loop.UpdateAtDistance(.1f, 1, 1, .4);
            for (int tick = 0; tick < 7; tick++) loop.UpdateAtDistance(.25f, 0, 1, .4);
            check(stops == 2 && disposals == 2, "Stopped equipment releases its sound after a short fade");
        }
        int before = requests.Count;
        var nearby = Enumerable.Range(0, 9).Select(_ => new LocalMachineLoop(api, new BlockPos(0, 0, 0), "airflow")).ToArray();
        foreach (var loop in nearby) loop.UpdateAtDistance(.1f, 1, 1, 0);
        check(requests.Count - before == 8, "Dense machinery loads at most eight nearby constant voices");
        nearby[0].Dispose();
        nearby[^1].UpdateAtDistance(.1f, 1, 1, 0);
        check(requests.Count - before == 9, "Unloading a source releases its voice budget for a waiting machine");
        foreach (var loop in nearby) loop.Dispose();
        duration = 8;
        before = starts;
        using (var shortLoop = new LocalMachineLoop(api, new BlockPos(0, 0, 0), "airflow"))
        {
            shortLoop.UpdateAtDistance(.1f, 1, 1, 0);
            shortLoop.UpdateAtDistance(.1f, 1, 1, 0);
            check(starts == before && errors == 1, "Short constant recordings fail once with a clear error and never start");
        }
        WorkMotion(check);
        MechanicalAudioFixture.Run(check);
    }

    private static void WorkMotion(Action<bool, string> check)
    {
        using var rig = new PneumaticTransportFixture.Rig();
        var sender = rig.Sender;
        sender.State.Loading = true; sender.State.Cargo = new ItemStack(rig.Item);
        sender.State.Parcel = Guid.NewGuid().ToString("N");
        var receiver = rig.Receiver;
        receiver.State.Cargo = new ItemStack(rig.Item); receiver.State.Parcel = Guid.NewGuid().ToString("N");
        check(PneumaticMachineSoundController.Describe(sender) is { Cue: "sender-work", Seconds: 1.6f } &&
            PneumaticMachineSoundController.Describe(receiver) is { Cue: "receiver-work", Seconds: 1.6f },
            "Sender lift/launch/return and Receiver gate/feed audio span the actual 1.6-second mechanisms");
        receiver.State.DeliveryOutlet = true;
        check(PneumaticMachineSoundController.Describe(receiver) == null,
            "A direct outlet does not invent a Receiver gate animation");
        var router = rig.Host(20, 2, "router");
        router.Router.ExpectedParcel = "incoming"; router.Router.ReadyInput = 2; router.Router.Preparation = .5;
        check(PneumaticMachineSoundController.Describe(router) is { Cue: "router-prepare", Seconds: .4f },
            "Router preparation has its own close/align/open sound phase");
        router.Router.Preparation = 1;
        check(PneumaticMachineSoundController.Describe(router)?.Progress == 1,
            "The Router opening sound retains its final preparation interval");
        router.State.Cargo = new ItemStack(rig.Item); router.State.Parcel = "parcel"; router.State.Progress = .8;
        router.Router.Lost = true;
        check(PneumaticMachineSoundController.Describe(router) is { Cue: "router-turn", Seconds: 1 } phase &&
            phase.Progress == PneumaticRouterMotion.CloseEnd,
            "A lost Router cannot play the carriage turn or launch phases ahead of its stopped gate");

        int starts = 0, pauses = 0, disposals = 0, errors = 0;
        List<SoundParams> requests = new();
        List<float> offsets = new(), gains = new(), pitches = new();
        var random = new Random(257);
        var logger = Stub.Create<ILogger>((method, _) => { if (method.Name == "Error") errors++; return null; });
        var world = Stub.Create<IClientWorldAccessor>((method, args) =>
        {
            if (method.Name == "get_Rand") return random;
            if (method.Name != "LoadSound") return null;
            var request = (SoundParams)args![0]!; requests.Add(request);
            float offset = 0;
            return Stub.Create<ILoadedSound>((soundMethod, soundArgs) =>
            {
                switch (soundMethod.Name)
                {
                    case "get_SoundLengthSeconds": return 1.6f;
                    case "get_PlaybackPosition": return offset;
                    case "Start": starts++; break;
                    case "Pause": pauses++; break;
                    case "Dispose": disposals++; break;
                    case "set_PlaybackPosition": offset = (float)soundArgs![0]!; offsets.Add(offset); break;
                    case "SetVolume": gains.Add((float)soundArgs![0]!); break;
                    case "SetPitch": pitches.Add((float)soundArgs![0]!); break;
                }
                return null;
            });
        });
        var api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
        { "get_World" => world, "get_Logger" => logger, _ => null });
        using var work = new PneumaticMachineSoundController(sender, api);
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(requests.Count == 0, "A reserved but stationary tray does not play an invented work click");
        sender.State.Progress = .0625;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        sender.State.Progress = .625;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        sender.State.Cargo = null; sender.State.Loading = false; sender.State.Returning = true; sender.State.ReturnProgress = .6875;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(starts == 1 && requests.Count == 1 && offsets[^1] >= .99f,
            "Cargo handoff continues the same Sender sound through empty return without replaying its lift");
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(pauses == 1 && gains[^1] == 0, "Stalled replicated motion pauses and silences the work sound once");
        sender.State.ReturnProgress = .75;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(starts == 2 && requests.Count == 1 && offsets[^1] >= 1.09f && pitches.All(p => p >= .25f && p <= 1.25f),
            "Restored air resumes audio at the current return phase with bounded playback speed");
        check(requests[0].Range == 5 && requests[0].ReferenceDistance == MachineSoundPolicy.InformationalReferenceDistance &&
            requests[0].ReferenceDistance < requests[0].Range && !requests[0].ShouldLoop &&
            gains.All(g => g >= 0 && g <= MachineSoundPolicy.InformationalVolume),
            "Full mechanical sequences retain the five-block informational range and reduced gain");
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 5);
        check(disposals == 1, "Leaving five blocks immediately releases mechanical audio");
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(requests.Count == 1, "Returning to a held machine does not replay its past action");
        sender.State.ReturnProgress = .8125;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(requests.Count == 2 && offsets[^1] >= 1.19f, "A newly audible moving source starts at its actual phase");
        check(requests[0].Location.ToString() == requests[1].Location.ToString(),
            "A stalled or temporarily distant mechanism retains the same acoustic variation through its cycle");
        sender.State.Returning = false;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(disposals == 2 && errors == 0, "Parking the machine releases its work sound without errors");
        sender.State.Loading = true; sender.State.Cargo = new ItemStack(rig.Item); sender.State.Progress = 0;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        sender.State.Progress = .0625;
        work.UpdateAtDistance(.1f, PneumaticMachineSoundController.Describe(sender), 1.2);
        check(requests.Count == 3 && requests[2].Location.ToString() != requests[1].Location.ToString() && offsets[^1] == 0,
            "A new batch starts a different full-cycle recording while preserving the model's phase timing");
    }
}
