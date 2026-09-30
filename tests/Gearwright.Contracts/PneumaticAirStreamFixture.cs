using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Pneumatics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class PneumaticAirStreamFixture
{
    private static readonly PneumaticAirSpecks.Source Source = new(new(0, -1, 0, 0), BlockFacing.EAST, true);
    private static PneumaticAirSpecks.Cell Tube(PneumaticPosition pos) => new("pipe-" + pos, BlockFacing.WEST, BlockFacing.EAST);
    private static void Travel(PneumaticAirSpecks specks, double seconds, System.Func<PneumaticPosition, PneumaticAirSpecks.Cell?> read)
    {
        while (seconds > 1e-10) { double step = Math.Min(.1, seconds); specks.Advance(step, read); seconds -= step; }
    }
    private sealed class LaneRandom : Random
    {
        private readonly double angle;
        private int calls;
        internal LaneRandom(double angle) => this.angle = angle;
        public override double NextDouble() => (calls++ % 3) switch { 0 => 1, 1 => angle, _ => .5 };
    }

    internal static void Run(Action<bool, string> check)
    {
        Emission(check);
        FullLineAndExit(check);
        Bends(check);
        Obstructions(check);
        UnpoweredTubes(check);
        LimitsAndLifecycle(check);
    }

    private static void Emission(Action<bool, string> check)
    {
        var specks = new PneumaticAirSpecks(); var random = new Random(473);
        PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => Tube(p);
        var times = new List<long>(); var speeds = new List<double>();
        bool atConnection = true;
        for (long now = 0; now <= 20000; now += 10)
        {
            specks.Advance(.01, Read);
            if (specks.Emit(Source, now, Read, random) is not { } id) continue;
            times.Add(now); specks.TryFrame(id, out var frame); speeds.Add(frame.Speed);
            atConnection &= Math.Abs(frame.Position.X) < 1e-9;
        }
        check(times.Count >= 30 && times.Count <= 100 && times[0] >= 200 && times[0] <= 610 &&
            times.Zip(times.Skip(1), (a, b) => b - a).All(gap => gap >= 200 && gap <= 610) && atConnection,
            "An accumulator emits at its connection every random 0.2–0.6 seconds, within one frame of the deadline");
        check(speeds.All(s => s >= 2.1 && s <= 2.5) && speeds.Max() - speeds.Min() > .2 && specks.Count > 1,
            "Each stream speck travels at the doubled 2.1–2.5 blocks/second speed and later specks can follow on the same line");
        int before = specks.Count;
        specks.Emit(Source, 100000, Read, random);
        check(specks.Count == before + 1, "A rendering hitch emits one speck instead of replaying a backlog");
        specks.Emit(Source with { Active = false }, 100500, Read, random);
        check(specks.Count == before + 1 && specks.TryStart(Source with { Active = false }, Read, random) == null,
            "An inactive accumulator stops new emissions without discarding the existing stream");
    }

    private static void FullLineAndExit(Action<bool, string> check)
    {
        var specks = new PneumaticAirSpecks();
        PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => p.X >= 0 && p.X < 128 ? Tube(p) : new PneumaticAirSpecks.Cell("");
        long id = specks.TryStart(Source, Read, new Random(21))!.Value;
        specks.TryFrame(id, out var initial);
        Travel(specks, 3, Read); specks.TryFrame(id, out var mid);
        check(!mid.Exiting && Math.Abs(mid.Position.X - 3 * initial.Speed) < 1e-8 && mid.Opacity == PneumaticAirSpecks.PeakOpacity,
            "Specks retain full size and opacity inside the line beyond the former short lifetime");
        Travel(specks, 128 / initial.Speed - 3, Read);
        bool atEnd = specks.TryFrame(id, out var end);
        check(atEnd && end.Exiting && Math.Abs(end.Position.X - 128) < 1e-7 && end.Size == PneumaticAirSpecks.PeakSize,
            "The same speck travels all 128 sections without a boundary pause and starts fading exactly at the open end");
        specks.Advance(.1, Read); specks.TryFrame(id, out var halfway);
        check(Math.Abs(halfway.Size - end.Size / 2) < 1e-6 && Math.Abs(halfway.Opacity - end.Opacity / 2) < 1e-6 &&
            Math.Abs(halfway.Position.X - (128 + initial.Speed * .1)) < 1e-7,
            "After 0.1 seconds outside, the moving exhaust speck has shrunk and faded by half");
        specks.Advance(.099, Read);
        check(specks.TryFrame(id, out var last) && last.Size < .001, "The exhaust speck remains only faintly visible until 0.2 seconds");
        specks.Advance(.001, Read);
        check(!specks.TryFrame(id, out _), "The exhaust speck disappears exactly 0.2 seconds after leaving the pipe");
    }

    private static void Bends(Action<bool, string> check)
    {
        var origin = new PneumaticPosition(0, 0, 0, 0);
        foreach (var input in BlockFacing.ALLFACES)
        foreach (var output in BlockFacing.ALLFACES.Where(f => f != input))
        {
            bool clear = true, continuous = true, dispersed = true, exited = false;
            for (int angle = 0; angle < 16; angle++)
            {
                var specks = new PneumaticAirSpecks();
                var source = new PneumaticAirSpecks.Source(origin.Offset(input), input.Opposite, true);
                var next = origin.Offset(output);
                var cells = new Dictionary<PneumaticPosition, PneumaticAirSpecks.Cell>
                { [origin] = new("bend", input, output), [next] = new("next", output.Opposite, output), [next.Offset(output)] = new("") };
                PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => cells.TryGetValue(p, out var cell) ? cell : null;
                long id = specks.TryStart(source, Read, new LaneRandom(angle / 16.0))!.Value;
                specks.TryFrame(id, out var initial);
                var startCenter = new Vec3d(.5 + input.Normali.X * .5, .5 + input.Normali.Y * .5, .5 + input.Normali.Z * .5);
                dispersed &= Math.Abs(initial.Position.SquareDistanceTo(startCenter) - .11 * .11) < 1e-8;
                Vec3d previous = initial.Position;
                for (int tick = 0; tick < 300; tick++)
                {
                    specks.Advance(.01, Read);
                    if (!specks.TryFrame(id, out var f)) continue;
                    var p = f.Position;
                    double alongInput = Math.Max(0, (p.X - .5) * input.Normali.X + (p.Y - .5) * input.Normali.Y + (p.Z - .5) * input.Normali.Z);
                    double alongOutput = Math.Max(0, (p.X - .5) * output.Normali.X + (p.Y - .5) * output.Normali.Y + (p.Z - .5) * output.Normali.Z);
                    var a = new Vec3d(.5 + input.Normali.X * alongInput, .5 + input.Normali.Y * alongInput, .5 + input.Normali.Z * alongInput);
                    var b = new Vec3d(.5 + output.Normali.X * alongOutput, .5 + output.Normali.Y * alongOutput, .5 + output.Normali.Z * alongOutput);
                    clear &= Math.Sqrt(Math.Min(p.SquareDistanceTo(a), p.SquareDistanceTo(b))) + f.Size * Math.Sqrt(3) / 2 < .2;
                    continuous &= p.SquareDistanceTo(previous) <= Math.Pow(f.Speed * .010001, 2);
                    if (alongOutput > .5) dispersed &= Math.Abs(p.SquareDistanceTo(b) - .11 * .11) < 1e-8;
                    exited |= f.Exiting; previous = p;
                }
            }
            check(clear && continuous && dispersed && exited,
                "Accumulator stream retains its offset lane through " + input.Code + " -> " + output.Code + ", the next joint and the open exit");
        }
    }

    private static void Obstructions(Action<bool, string> check)
    {
        foreach (bool newlyPlaced in new[] { false, true })
        {
            var specks = new PneumaticAirSpecks(); bool blocked = !newlyPlaced;
            PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => p.X == 0 ? Tube(p) : blocked ? null : new PneumaticAirSpecks.Cell("");
            long id = specks.TryStart(Source, Read, new Random(12))!.Value;
            Travel(specks, .3, Read); blocked = true;
            bool clear = true;
            for (int i = 0; i < 150; i++)
            {
                specks.Advance(.01, Read);
                if (specks.TryFrame(id, out var frame)) clear &= frame.Position.X + frame.Size / 2 < 1 && !frame.Exiting;
            }
            check(clear && specks.Count == 0, "A " + (newlyPlaced ? "new" : "pre-existing") + " inventory or obstruction stops the stream before its block");
        }
        {
            var specks = new PneumaticAirSpecks(); bool removedBehind = false;
            PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => removedBehind && p.X == 0 ? null : Tube(p);
            long id = specks.TryStart(Source, Read, new Random(7))!.Value;
            Travel(specks, 2, Read); removedBehind = true; specks.Advance(.1, Read);
            check(specks.TryFrame(id, out var frame) && frame.Position.X > 2,
                "Removing a section behind a speck does not erase a stream already downstream");
        }
        {
            var specks = new PneumaticAirSpecks(); bool occupied = false;
            PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => occupied ? null : new PneumaticAirSpecks.Cell("");
            long id = specks.TryStart(Source, Read, new Random(4))!.Value;
            specks.Advance(.05, Read); occupied = true; specks.Advance(.01, Read);
            check(!specks.TryFrame(id, out _), "Placing a block in open exhaust immediately removes the speck before it can cross the block");
        }
        {
            var specks = new PneumaticAirSpecks();
            check(specks.TryStart(Source, _ => null, new Random(0)) == null &&
                specks.TryStart(Source, _ => new("wrong-way", BlockFacing.NORTH, BlockFacing.SOUTH), new Random(0)) == null,
                "An accumulator cannot emit into an unloaded, blocked or mismatched connection");
        }
    }

    private static void UnpoweredTubes(Action<bool, string> check)
    {
        foreach (var direction in BlockFacing.ALLFACES)
        {
            var origin = new PneumaticPosition(0, 0, 0, 0);
            var next = origin.Offset(direction);
            var hosts = new Dictionary<PneumaticPosition, BlockEntityPneumaticTransport>();
            foreach (var position in new[] { origin, next, next.Offset(direction) })
            {
                var host = new BlockEntityPneumaticTransport
                { Air = 1, Pos = new(position.X, position.Y, position.Z, position.Dimension) };
                Set(host, "Block", new Block { Code = new("gearwright:pneumatic-tube") });
                host.State.Input = direction.Opposite; host.State.Output = direction;
                hosts[position] = host;
            }
            var chunk = Stub.Create<IWorldChunk>((_, _) => null);
            var empty = new Block { BlockId = 0 };
            var accessor = Stub.Create<IBlockAccessor>((m, args) => m.Name switch
            {
                "GetChunkAtBlockPos" => chunk,
                "GetBlockEntity" => hosts.GetValueOrDefault(PneumaticNetworkSystem.Position((BlockPos)args[0])),
                "GetBlock" => empty,
                _ => null
            });
            var world = Stub.Create<IWorldAccessor>((m, _) => m.Name == "get_BlockAccessor" ? accessor : null);
            PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => PneumaticAirSpecks.ReadCell(world, p);
            var source = new PneumaticAirSpecks.Source(origin.Offset(direction.Opposite), direction, true);
            var specks = new PneumaticAirSpecks(); var random = new Random(91);
            string label = " (" + direction.Code + ")";
            hosts[origin].Air = 0;
            check(specks.TryStart(source, Read, random) == null,
                "No speck spawns into an unpowered first tube" + label);

            hosts[origin].Air = 1; hosts[next].Air = 0;
            long id = specks.TryStart(source, Read, random)!.Value;
            specks.TryFrame(id, out var start);
            Travel(specks, (1 - PneumaticAirSpecks.Clearance) / start.Speed, Read);
            specks.Advance(.1, Read);
            bool visible = specks.TryFrame(id, out var stopped);
            var n = direction.Normali;
            double distance = (stopped.Position.X - start.Position.X) * n.X +
                (stopped.Position.Y - start.Position.Y) * n.Y + (stopped.Position.Z - start.Position.Z) * n.Z;
            check(visible && !stopped.Exiting && Math.Abs(distance - .92) < 1e-8 && distance + stopped.Size / 2 < 1 &&
                Math.Abs(stopped.Opacity - PneumaticAirSpecks.PeakOpacity / 2) < 1e-6,
                "A speck stops and fades before an unpowered tube, even with a powered tube beyond it" + label);
            specks.Advance(.1, Read);
            check(!specks.TryFrame(id, out _) && specks.Count == 0,
                "The blocked speck despawns after its 0.2-second fade" + label);

            hosts[next].Air = 1;
            long approaching = specks.TryStart(source, Read, random)!.Value;
            specks.TryFrame(approaching, out start);
            Travel(specks, .95 / start.Speed, Read);
            hosts[next].Air = 0; specks.Advance(.1, Read);
            check(!specks.TryFrame(approaching, out _),
                "Losing power just before a joint removes the approaching speck before it crosses" + label);

            hosts[next].Air = 1;
            long crossing = specks.TryStart(source, Read, random)!.Value;
            specks.TryFrame(crossing, out start);
            Travel(specks, 1.2 / start.Speed, Read);
            check(specks.TryFrame(crossing, out _), "Restoring power permits a new speck to cross the joint" + label);
            hosts[next].Air = 0; specks.Advance(.01, Read);
            check(!specks.TryFrame(crossing, out _) && specks.Count == 0,
                "A speck already inside a tube despawns on the next frame when that tube loses power" + label);
            hosts[next].Air = 1; specks.Advance(.1, Read);
            check(specks.Count == 0, "Restoring power does not revive expired specks" + label);
        }
    }

    private static void LimitsAndLifecycle(Action<bool, string> check)
    {
        var specks = new PneumaticAirSpecks();
        PneumaticAirSpecks.Cell? Read(PneumaticPosition p) => Tube(p);
        long id = specks.TryStart(Source, Read, new Random(11))!.Value; specks.TryFrame(id, out var frame);
        Travel(specks, PneumaticAir.MaximumNodes / frame.Speed + .1, Read);
        check(specks.Count == 0, "A malformed endless route stops at the network's 1024-section bound");
        var random = new Random(1);
        for (int i = 0; i < PneumaticAirSpecks.MaximumFlights; i++) specks.TryStart(Source, Read, random);
        check(specks.Count == PneumaticAirSpecks.MaximumFlights && specks.TryStart(Source, Read, random) == null,
            "The stream cache is bounded while accommodating full-length lines at the fastest emission interval");
        specks.Dispose(); check(specks.Count == 0, "World shutdown releases the complete stream cache");

        int registered = 0, unregistered = 0;
        var events = Stub.Create<IClientEventAPI>((m, _) => { if (m.Name == "RegisterRenderer") registered++; if (m.Name == "UnregisterRenderer") unregistered++; return null; });
        var world = Stub.Create<IClientWorldAccessor>((_, _) => null);
        var api = Stub.Create<ICoreClientAPI>((m, _) => m.Name switch { "get_World" => world, "get_Event" => events, _ => null });
        var source = new BlockEntityPneumaticAirIntake { Pos = new(-1, 0, 0) };
        var pipe = new BlockEntityPneumaticTransport { Pos = new(0, 0, 0) };
        var first = PneumaticAirSpecks.Acquire(api, source); var second = PneumaticAirSpecks.Acquire(api, pipe);
        first.Release(source);
        check(ReferenceEquals(first, second) && registered == 1 && unregistered == 0,
            "One world renderer keeps the downstream stream alive when the accumulator renderer unloads");
        second.Release(pipe);
        check(unregistered == 1, "The last pneumatic renderer unregisters and disposes the shared air renderer");
    }
}
