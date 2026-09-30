using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Client-only streams from accumulator outlets through loaded mainlines.</summary>
internal sealed class PneumaticAirSpecks : IRenderer
{
    internal const double MinimumSpeed = 2.1, MaximumSpeed = 2.5, ExitFade = .2, Clearance = .08, LaneRadius = .11;
    internal const float PeakSize = .36f / 16, PeakOpacity = .42f;
    // Enough for the full 1024-node network even at the shortest emission interval.
    internal const int MaximumFlights = 8192;
    internal readonly record struct Cell(string Identity, BlockFacing? Input = null, BlockFacing? Output = null,
        int Outputs = 0, BlockFacing? Mount = null, BlockFacing? Forward = null)
    {
        internal bool Open => Input == null && Output == null && Outputs == 0;
    }
    internal readonly record struct Source(PneumaticPosition Position, BlockFacing Outlet, bool Active);
    internal readonly record struct Frame(Vec3d Position, int Dimension, float Size, float Opacity, double Speed, bool Exiting);
    private enum Phase { Pipe, Exhaust, Blocked }
    private sealed class Segment
    {
        internal double Length;
        internal Vec3d ExitLane = new();
        internal readonly List<(Vec3d Point, double Distance)> Points = new();
        internal void Add(Vec3d point)
        {
            if (Points.Count > 0) Length += Math.Sqrt(Points[^1].Point.SquareDistanceTo(point));
            Points.Add((point, Length));
        }
        internal Vec3d At(double distance)
        {
            for (int i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1]; var b = Points[i];
                if (distance > b.Distance || b.Distance - a.Distance < 1e-9) continue;
                double t = Math.Clamp((distance - a.Distance) / (b.Distance - a.Distance), 0, 1);
                return new(a.Point.X + (b.Point.X - a.Point.X) * t,
                    a.Point.Y + (b.Point.Y - a.Point.Y) * t, a.Point.Z + (b.Point.Z - a.Point.Z) * t);
            }
            return Points[^1].Point.Clone();
        }
    }
    private sealed class Flight
    {
        internal PneumaticPosition Position;
        internal Cell Cell;
        internal BlockFacing Direction = BlockFacing.EAST;
        internal Segment? Segment;
        internal Vec3d Point = new();
        internal double Speed, Distance, Age, FadeAge;
        internal int Hops;
        internal long Choice;
        internal Phase Mode;
        internal Frame Frame
        {
            get
            {
                double fade = Mode == Phase.Pipe ? 1 : 1 - PneumaticRenderer.Smooth(FadeAge / ExitFade);
                double birth = PneumaticRenderer.Smooth(Age / .12);
                return new(Point.Clone(), Position.Dimension, PeakSize * (float)((.3 + .7 * birth) * fade),
                    PeakOpacity * (float)(birth * fade), Speed, Mode == Phase.Exhaust);
            }
        }
    }

    private static readonly ConditionalWeakTable<IWorldAccessor, PneumaticAirSpecks> worlds = new();
    private readonly Dictionary<long, Flight> flights = new();
    private readonly Dictionary<PneumaticPosition, long> emissionTimes = new();
    private readonly Dictionary<PneumaticPosition, BlockEntityPneumaticAirIntake> sources = new();
    private readonly Dictionary<PneumaticPosition, Cell?> cells = new();
    private readonly List<long> expired = new();
    private ICoreClientAPI? api;
    private MeshRef? mesh;
    private LoadedTexture? texture;
    private long nextId;
    private int users;
    private bool registered;
    internal int Count => flights.Count;
    public double RenderOrder => .56;
    public int RenderRange => 48;

    internal static PneumaticAirSpecks Acquire(ICoreClientAPI api, BlockEntity entity)
    {
        var specks = worlds.GetValue(api.World, _ => new());
        specks.api = api; specks.users++;
        if (entity is BlockEntityPneumaticAirIntake source)
            specks.sources[PneumaticNetworkSystem.Position(source.Pos)] = source;
        if (!specks.registered)
        {
            api.Event.RegisterRenderer(specks, EnumRenderStage.Opaque, "gearwright-pneumatic-air");
            specks.registered = true;
        }
        return specks;
    }

    internal static Cell? ReadCell(IWorldAccessor world, PneumaticPosition position)
    {
        var pos = new BlockPos(position.X, position.Y, position.Z, position.Dimension);
        var blocks = world.BlockAccessor;
        if (blocks.GetChunkAtBlockPos(pos) == null) return null;
        var entity = blocks.GetBlockEntity(pos);
        if (entity is BlockEntityPneumaticTransport host)
        {
            if (!host.Node.Valid || !host.CanWrite || host.Air <= 0) return null;
            if (host.Kind != PneumaticLineKind.Router) return new Cell(host.State.Instance, host.State.Input, host.State.Output);
            int mask = 0;
            foreach (var output in host.RouterPorts("output")) mask |= 1 << output.Index;
            return host.Router.SupplyPort > 0 && mask != 0 ? new Cell(host.State.Instance, host.Router.Face(host.Router.SupplyPort),
                null, mask, host.Router.Mount, host.Router.Forward) : null;
        }
        // Sender/receiver mainlines are connected conduits. Inventories and
        // every other occupied cell stop specks even if their collision box is inset.
        return blocks.GetBlock(pos).Id == 0 && entity == null ? new Cell("") : null;
    }

    internal long? Emit(Source source, long now, System.Func<PneumaticPosition, Cell?> read, Random random)
    {
        if (!source.Active) { emissionTimes.Remove(source.Position); return null; }
        if (!emissionTimes.TryGetValue(source.Position, out long due))
        { emissionTimes[source.Position] = now + Interval(random); return null; }
        if (now < due) return null;
        // One emission per callback; never replay a backlog after a hitch.
        emissionTimes[source.Position] = now + Interval(random);
        return TryStart(source, read, random);
    }

    private static long Interval(Random random) => (long)Math.Ceiling(200 + random.NextDouble() * 400);

    internal long? TryStart(Source source, System.Func<PneumaticPosition, Cell?> read, Random random)
    {
        if (!source.Active || flights.Count >= MaximumFlights) return null;
        var pos = source.Position.Offset(source.Outlet);
        if (read(pos) is not { } cell || !cell.Open && cell.Input != source.Outlet.Opposite) return null;
        double radius = LaneRadius * Math.Sqrt(random.NextDouble()), angle = random.NextDouble() * Math.PI * 2;
        double u = radius * Math.Cos(angle), v = radius * Math.Sin(angle);
        var n = source.Outlet.Normali;
        var lane = n.X != 0 ? new Vec3d(0, u, v) : n.Y != 0 ? new Vec3d(u, 0, v) : new Vec3d(u, v, 0);
        var flight = new Flight
        {
            Position = pos, Cell = cell, Direction = source.Outlet, Hops = cell.Open ? 0 : 1,
            Choice = random.Next(1 << 20),
            Speed = MinimumSpeed + (MaximumSpeed - MinimumSpeed) * random.NextDouble(),
            Point = new Vec3d(source.Position.X + .5 + n.X * .5, source.Position.Y + .5 + n.Y * .5,
                source.Position.Z + .5 + n.Z * .5).Add(lane)
        };
        if (cell.Open) flight.Mode = Phase.Exhaust;
        else { flight.Direction = Output(cell, flight.Choice); flight.Segment = BuildSegment(pos, cell, lane, flight.Direction); }
        long id = ++nextId; flights[id] = flight; return id;
    }

    private static BlockFacing Output(Cell cell, long id)
    {
        if (cell.Output != null) return cell.Output;
        var choices = new List<BlockFacing>();
        foreach (var face in BlockFacing.ALLFACES) if ((cell.Outputs & (1 << face.Index)) != 0) choices.Add(face);
        return choices[(int)(id % choices.Count)];
    }
    private static Segment BuildSegment(PneumaticPosition pos, Cell cell, Vec3d lane, BlockFacing output)
    {
        if (cell.Outputs != 0) return BypassSegment(pos, cell, lane, output);
        var segment = new Segment();
        var center = new Vec3d(pos.X + .5, pos.Y + .5, pos.Z + .5);
        Vec3d Face(BlockFacing face, double offset) => center.Clone().Add(
            face.Normali.X * offset, face.Normali.Y * offset, face.Normali.Z * offset);
        segment.Add(Face(cell.Input!, .5).Add(lane));
        if (cell.Input!.Opposite != cell.Output)
        {
            var a = Face(cell.Input, .25); var b = Face(cell.Output!, .25);
            var incoming = cell.Input.Opposite.Normali; var outgoing = cell.Output!.Normali;
            var axis = new Vec3d(incoming.Y * outgoing.Z - incoming.Z * outgoing.Y,
                incoming.Z * outgoing.X - incoming.X * outgoing.Z, incoming.X * outgoing.Y - incoming.Y * outgoing.X);
            segment.Add(a.Clone().Add(lane));
            for (int i = 1; i <= 8; i++)
            {
                double t = i / 8.0, s = 1 - t;
                var point = new Vec3d(a.X * s * s + 2 * center.X * s * t + b.X * t * t,
                    a.Y * s * s + 2 * center.Y * s * t + b.Y * t * t,
                    a.Z * s * s + 2 * center.Z * s * t + b.Z * t * t);
                segment.Add(point.Add(RotateLane(lane, axis, Math.Atan2(t, s))));
            }
            lane = RotateLane(lane, axis, Math.PI / 2);
        }
        segment.Add(Face(cell.Output!, .5).Add(lane)); segment.ExitLane = lane;
        return segment;
    }

    private static Segment BypassSegment(PneumaticPosition pos, Cell cell, Vec3d lane, BlockFacing output)
    {
        var router = new PneumaticRouterState { Mount = cell.Mount!, Forward = cell.Forward! };
        var matrix = router.Matrix; int start = (router.Port(cell.Input!) - 1) * 2, end = (router.Port(output) - 1) * 2;
        const double a = 1.58, b = 14.42, y = 2.425;
        var ring = new[] { (a, 8.3), (a, b), (8.3, b), (b, b), (b, 7.7), (b, a), (7.7, a), (a, a) };
        var segment = new Segment();
        Vec3d World(double x, double yy, double z)
        {
            var v = Mat4f.MulWithVec4(matrix, new[] { (float)(x / 16), (float)(yy / 16), (float)(z / 16), 1f });
            return new(pos.X + v[0], pos.Y + v[1], pos.Z + v[2]);
        }
        Vec3d Face(BlockFacing f) => new(pos.X + .5 + f.Normali.X * .5,
            pos.Y + .5 + f.Normali.Y * .5, pos.Z + .5 + f.Normali.Z * .5);
        segment.Add(Face(cell.Input!).Add(lane));
        var first = ring[start]; segment.Add(World(first.Item1, 5.9, first.Item2)); segment.Add(World(first.Item1, y, first.Item2));
        int clockwise = (end - start + 8) % 8, direction = clockwise <= 4 ? 1 : -1, steps = direction > 0 ? clockwise : 8 - clockwise;
        for (int i = 1; i <= steps; i++) { var point = ring[(start + direction * i + 8) % 8]; segment.Add(World(point.Item1, y, point.Item2)); }
        var last = ring[end]; segment.Add(World(last.Item1, 5.9, last.Item2));
        // A narrow header recentres the stream; the following tube can retain
        // this small offset without passing through the header's walls.
        segment.ExitLane = new(); segment.Add(Face(output)); return segment;
    }

    private static Vec3d RotateLane(Vec3d lane, Vec3d axis, double angle)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle), dot = lane.X * axis.X + lane.Y * axis.Y + lane.Z * axis.Z;
        return new(lane.X * c + (axis.Y * lane.Z - axis.Z * lane.Y) * s + axis.X * dot * (1 - c),
            lane.Y * c + (axis.Z * lane.X - axis.X * lane.Z) * s + axis.Y * dot * (1 - c),
            lane.Z * c + (axis.X * lane.Y - axis.Y * lane.X) * s + axis.Z * dot * (1 - c));
    }

    internal void Advance(double seconds, System.Func<PneumaticPosition, Cell?> read)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        seconds = Math.Min(seconds, .1);
        expired.Clear();
        foreach (var pair in flights)
            if (!Advance(pair.Value, seconds, read)) expired.Add(pair.Key);
        foreach (long id in expired) flights.Remove(id);
    }

    private static bool Advance(Flight flight, double seconds, System.Func<PneumaticPosition, Cell?> read)
    {
        flight.Age += seconds;
        // Carry unused time across each shared face instead of pausing there.
        for (int transition = 0; transition < 4 && seconds > 1e-10; transition++)
        {
            if (read(flight.Position) != flight.Cell) return false;
            if (flight.Mode != Phase.Pipe)
            {
                flight.FadeAge += seconds;
                if (flight.FadeAge >= ExitFade - 1e-10) return false;
                if (flight.Mode == Phase.Exhaust)
                {
                    var n = flight.Direction.Normali;
                    flight.Point.Add(n.X * flight.Speed * seconds, n.Y * flight.Speed * seconds, n.Z * flight.Speed * seconds);
                }
                return true;
            }
            var segment = flight.Segment!;
            var next = flight.Position.Offset(flight.Direction);
            Cell? following = read(next);
            bool connected = following is { Open: false } cell && cell.Input == flight.Direction.Opposite;
            bool open = following is { Open: true };
            double end = segment.Length - (connected || open ? 0 : Clearance);
            if (flight.Distance > end + 1e-9) return false; // new obstruction immediately beside a speck
            double step = Math.Min(seconds, Math.Max(0, end - flight.Distance) / flight.Speed);
            flight.Distance += step * flight.Speed;
            flight.Point = segment.At(flight.Distance); seconds -= step;
            if (flight.Distance < end - 1e-9) return true;
            if (!connected && !open) { flight.Mode = Phase.Blocked; continue; }
            flight.Position = next; flight.Cell = following!.Value; flight.Distance = 0;
            if (open) { flight.Mode = Phase.Exhaust; flight.Segment = null; continue; }
            if (++flight.Hops > PneumaticAir.MaximumNodes) return false;
            flight.Direction = Output(flight.Cell, flight.Choice + flight.Hops);
            flight.Segment = BuildSegment(next, flight.Cell, segment.ExitLane, flight.Direction);
        }
        return true;
    }

    internal bool TryFrame(long id, out Frame frame)
    {
        if (flights.TryGetValue(id, out var flight)) { frame = flight.Frame; return true; }
        frame = default; return false;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (api == null) return;
        cells.Clear();
        Cell? Read(PneumaticPosition pos)
        {
            if (!cells.TryGetValue(pos, out var cell)) cells[pos] = cell = ReadCell(api.World, pos);
            return cell;
        }
        Advance(dt, Read);
        foreach (var pair in sources)
        {
            var source = pair.Value;
            bool active = source.CanWriteState && source.StoredAir > 0 &&
                api.World.BlockAccessor.GetChunkAtBlockPos(source.Pos) != null;
            Emit(new(pair.Key, source.Outlet, active), api.World.ElapsedMilliseconds, Read, api.World.Rand);
        }
        var camera = api.World.Player.Entity.CameraPos;
        foreach (var flight in flights.Values)
        {
            if (flight.Position.Dimension != api.World.Player.Entity.Pos.Dimension ||
                flight.Point.SquareDistanceTo(camera) > RenderRange * RenderRange) continue;
            var frame = flight.Frame;
            if (frame.Opacity < .001) continue;
            Draw(frame, api, camera);
        }
    }

    private void Draw(Frame frame, ICoreClientAPI api, Vec3d camera)
    {
        if (mesh == null)
        {
            mesh = api.Render.UploadMesh(CubeMeshUtil.GetCube(.5f, .5f, new Vec3f()));
            var white = new LoadedTexture(api) { Width = 1, Height = 1 };
            api.Render.LoadOrUpdateTextureFromRgba(new[] { -1 }, false, 0, ref white); texture = white;
        }
        var p = frame.Position;
        var shader = api.Render.PreparedStandardShader((int)Math.Floor(p.X), (int)Math.Floor(p.Y), (int)Math.Floor(p.Z), new Vec4f(1, 1, 1, 1));
        shader.ViewMatrix = api.Render.CameraMatrixOriginf; shader.ProjectionMatrix = api.Render.CurrentProjectionMatrix;
        api.Render.GlToggleBlend(true, EnumBlendMode.Standard); api.Render.GLDepthMask(false); api.Render.GlEnableCullFace();
        try
        {
            shader.AlphaTest = .001f; shader.RgbaTint = new Vec4f(.66f, .83f, 1, frame.Opacity); shader.Tex2D = texture!.TextureId;
            shader.ModelMatrix = new Matrixf().Identity().Translate(p.X - camera.X, p.Y - camera.Y, p.Z - camera.Z)
                .Scale(frame.Size, frame.Size, frame.Size).Values;
            api.Render.RenderMesh(mesh);
        }
        finally { shader.Stop(); api.Render.GLDepthMask(true); api.Render.GlToggleBlend(false); }
    }

    internal void Release(BlockEntity entity)
    {
        if (entity is BlockEntityPneumaticAirIntake source)
        {
            var pos = PneumaticNetworkSystem.Position(source.Pos);
            if (sources.TryGetValue(pos, out var current) && ReferenceEquals(source, current))
            { sources.Remove(pos); emissionTimes.Remove(pos); }
        }
        if (--users != 0) return;
        if (registered) api?.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        Dispose();
    }

    public void Dispose()
    {
        registered = false; flights.Clear(); sources.Clear(); emissionTimes.Clear(); cells.Clear(); expired.Clear();
        mesh?.Dispose(); mesh = null; texture?.Dispose(); texture = null;
    }
}
