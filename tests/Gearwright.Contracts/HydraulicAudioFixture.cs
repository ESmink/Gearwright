using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Audio;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class HydraulicAudioFixture
{
    internal static void Run(Action<bool, string> check)
    {
        var rig = new Rig();
        var pipe = new BlockEntityFluidPipe { Pos = new(0, 0, 0) };
        using (var controller = new HydraulicPipeSoundController(pipe, rig.Api))
        {
            foreach (double pressure in new[] { 0, 20, 100, 150, 250, 499.99, 500 })
            {
                SetWater(pipe, pressure);
                for (int i = 0; i < 30; i++) rig.Tick(controller, 1);
            }
            check(rig.Voices.All(v => v.Cue == "water-pipe") && rig.Voices.Count == 1,
                "Moving liquid through 500 kPa, including full sprinkler output, plays only water flow");
            SetWater(pipe, 1250);
            for (int i = 0; i < 19; i++) rig.Tick(controller, 1);
            check(rig.Creaks.Count == 0, "A short high-pressure spike does not start pipe strain audio");
            SetWater(pipe, 20); rig.Tick(controller, 1);
            SetWater(pipe, 1250);
            for (int i = 0; i < 19; i++) rig.Tick(controller, 1);
            check(rig.Creaks.Count == 0, "Returning to low pressure resets the full sustained-pressure delay");
            rig.Tick(controller, 1);
            check(rig.Creaks.Count == 1 && rig.Creaks[0].Playing && rig.Creaks[0].Parameters.Range == 5,
                "Five seconds above the warning threshold allows one restrained pressure creak");
            check(rig.Creaks[0].Parameters.Volume < .10f,
                "Even 1250 kPa starts with restrained strain volume, not a full-pressure warning");
            SetWater(pipe, 500); rig.Tick(controller, 1);
            check(rig.Creaks[0].Disposed && !rig.Creaks[0].Playing,
                "Dropping to normal pressure immediately stops an already playing creak");
            SetWater(pipe, 1250);
            for (int i = 0; i < 20; i++) rig.Tick(controller, 1);
            check(rig.Creaks.Count == 1, "Pressure recovery still respects the shared creak cooldown");
            rig.Now += 120_000; rig.Tick(controller, 1);
            check(rig.Creaks.Count == 2, "Sustained high pressure can repeat after its bounded cooldown");
            rig.Tick(controller, 5);
            check(rig.Voices.All(v => v.Disposed), "Leaving range releases both water and pressure sounds");
        }
        rig = new Rig();
        var irrigator = new BlockEntityIrrigatorPipe { Pos = new(0, 0, 0) };
        using (var controller = new HydraulicPipeSoundController(irrigator, rig.Api))
        {
            SetWater(irrigator, 20);
            for (int i = 0; i < 16; i++) rig.Tick(controller, 3.5);
            check(rig.Voices.Count == 1 && rig.Voices[0].Cue == "water-irrigator" &&
                rig.Voices[0].Parameters.Range == 5 && rig.Voices[0].Parameters.ReferenceDistance == 4.9f &&
                rig.Voices[0].Volume > .3f,
                "Overhead irrigation remains audible across crops at 3.5 blocks with one distance fade");
            rig.Tick(controller, 1);
            check(rig.Voices.Count == 1, "An active irrigator uses one water voice instead of a redundant pipe bed");
            for (int i = 0; i < 600; i++)
            {
                SetWater(irrigator, i % 2 == 0 ? 20 : 18);
                rig.Tick(controller, 3.5, .1f);
            }
            Voice irrigation = rig.Voices.Single();
            check(irrigation.Starts == 1 && irrigation.Stops == 0 && irrigation.Seeks == 1 && irrigation.Playing,
                "A minute of ten-per-second irrigation state updates keeps one uninterrupted voice and playback position");
            for (int i = 0; i < 60; i++)
            {
                SetWater(irrigator, i % 2 == 0 ? 0 : 20);
                rig.Tick(controller, 3.5, .1f);
            }
            check(irrigation.Starts == 1 && irrigation.Stops == 0 && irrigation.Seeks == 1 && irrigation.Playing,
                "Brief pressure dips fade the existing irrigation voice without restarting or seeking it");
            SetWater(irrigator, 0);
            for (int i = 0; i < 16; i++) rig.Tick(controller, 3.5, .1f);
            check(irrigation.Disposed && irrigation.Stops == 1,
                "Sustained loss of pressure still releases the irrigation voice");
            SetWater(irrigator, 20); rig.Tick(controller, 3.5, .1f);
            check(rig.Voices.Count(v => v.Cue == "water-irrigator") == 2 && rig.Voices.Last().Starts == 1,
                "Restoring irrigation after a real stop starts one new voice");
            rig.Tick(controller, 5);
            check(rig.Voices.All(v => v.Disposed), "Irrigation stops at its explicit five-block boundary");
            SetWater(irrigator, 0); rig.Tick(controller, 1);
            check(rig.Voices.All(v => v.Cue != "water-irrigator" || !v.Playing),
                "An unpressurized irrigator cannot invent falling water");
        }
        rig = new Rig();
        foreach (string cue in new[] { "water-sprinkler", "water-irrigator", "water-pipe", "airflow" })
        {
            using var loop = new LocalMachineLoop(rig.Api, new(0, 0, 0), cue);
            int before = rig.Voices.Count;
            loop.UpdateAtDistance(.25f, 1, 1, 3);
            check(rig.Voices.Count - before == (cue is "water-sprinkler" or "water-irrigator" ? 1 : 0),
                "Only irrigation receives the larger continuous sound range: " + cue);
        }
    }

    private static void SetWater(BlockEntityFluidPipe pipe, double pressure) =>
        pipe.ApplySimulationState(new("game:waterportion"), 10, 20, pressure, "running", null, 1, new double[6], 0);

    private sealed class Rig
    {
        internal readonly List<Voice> Voices = new();
        internal List<Voice> Creaks => Voices.Where(v => v.Cue.StartsWith("pressure-creak")).ToList();
        internal readonly ICoreClientAPI Api;
        internal long Now;
        internal Rig()
        {
            var cache = new Dictionary<string, object>();
            var random = new Random(785);
            var world = Stub.Create<IClientWorldAccessor>((method, args) =>
            {
                if (method.Name == "get_Rand") return random;
                if (method.Name == "get_ElapsedMilliseconds") return Now;
                if (method.Name != "LoadSound") return null;
                var voice = new Voice((SoundParams)args![0]!); Voices.Add(voice); return voice.Sound;
            });
            Api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
            { "get_World" => world, "get_ObjectCache" => cache, _ => null });
        }
        internal void Tick(HydraulicPipeSoundController controller, double distance, float seconds = .25f)
        { Now += (long)(seconds * 1000); controller.UpdateAtDistance(seconds, distance); }
    }

    private sealed class Voice
    {
        internal readonly SoundParams Parameters;
        internal readonly ILoadedSound Sound;
        internal readonly string Cue;
        internal bool Playing, Disposed;
        internal int Starts, Stops, Seeks;
        internal float Volume;
        internal Voice(SoundParams parameters)
        {
            Parameters = parameters; Cue = parameters.Location.Path.Split('/').Last().Replace(".ogg", "");
            Sound = Stub.Create<ILoadedSound>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_SoundLengthSeconds": return parameters.ShouldLoop ? 240f : 1.8f;
                    case "Start": Starts++; Playing = true; break;
                    case "Stop": Stops++; Playing = false; break;
                    case "set_PlaybackPosition": Seeks++; break;
                    case "Dispose": Disposed = true; Playing = false; break;
                    case "SetVolume": Volume = (float)args![0]!; break;
                }
                return null;
            });
        }
    }
}
