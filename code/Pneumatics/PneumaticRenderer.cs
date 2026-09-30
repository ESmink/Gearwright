using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Approved cuboids posed from server progress. Rendering never creates cargo or air.</summary>
internal sealed class PneumaticRenderer : IRenderer
{
    private readonly BlockEntity entity;
    private readonly ICoreClientAPI api;
    private Shape? shape;
    private string prepared = "";
    private List<Bone> bones = new();
    private AnimationKeyFrame[] frames = Array.Empty<AnimationKeyFrame>();
    private double shownProgress, priorProgress, targetProgress, interpolationAge;
    private string presentationKey = "";
    private readonly PneumaticCargoMotion cargoMotion;
    private readonly PneumaticAirSpecks airSpecks;
    private readonly PneumaticRouterPresentation routerPresentation = new();
    private bool disposed;
    private sealed class Bone
    {
        internal int Parent = -1;
        internal ShapeElement? Element;
        internal float[] Ancestors = Mat4f.Create(), World = Mat4f.Create();
        internal MeshData Mesh = new(4, 6);
        internal MeshData Glass = new(4, 6);
        internal MultiTextureMeshRef? Uploaded;
        internal MultiTextureMeshRef? UploadedGlass;
    }
    internal PneumaticRenderer(BlockEntity entity, ICoreClientAPI api)
    {
        this.entity = entity; this.api = api;
        cargoMotion = PneumaticCargoMotion.For(api.World);
        airSpecks = PneumaticAirSpecks.Acquire(api, entity);
        OnStateUpdated();
    }

    internal void OnStateUpdated()
    {
        if (entity is not BlockEntityPneumaticTransport host) return;
        if (host.Kind == PneumaticLineKind.Router) routerPresentation.Observe(PneumaticRouterMotion.Pose(host), api.World.ElapsedMilliseconds);
        if (host.State.Cargo == null) return;
        if (host.Kind == PneumaticLineKind.Router)
        {
            // Freeze authoritative geometry for this packet; the stopped item
            // follows the same displayed carriage angle as the moving bones.
            int inlet = host.Router.TransitInput, outlet = host.Router.TransitOutput;
            bool lost = host.Router.Lost;
            var matrix = host.Router.Matrix;
            int x = host.Pos.X, y = host.Pos.Y, z = host.Pos.Z;
            Vec3d Path(double p, long time)
            {
                var c = PneumaticRouterMotion.Cargo(p, inlet, outlet, lost, routerPresentation.At(time).Angle);
                var point = Mat4f.MulWithVec4(matrix, new[] { (float)c.X, (float)c.Y, (float)c.Z, 1f });
                return new Vec3d(point[0] + x, point[1] + y, point[2] + z);
            }
            cargoMotion.ObservePath(host.State.Parcel, host.State.Instance, host.State.RouteIndex,
                host.State.Progress, Path, api.World.ElapsedMilliseconds);
            return;
        }
        var local = CargoPosition(host.State, host.Kind, host.IsReceiving);
        if (host.State.Loading || host.IsReceiving)
        {
            var matrix = PneumaticPlacement.EndpointMatrix(host.State.Output, host.State.InventoryFace);
            var point = Mat4f.MulWithVec4(matrix, new[] { (float)local.X, (float)local.Y, (float)local.Z, 1f });
            local.Set(point[0], point[1], point[2]);
        }
        cargoMotion.Observe(host.State.Parcel, host.State.Instance, host.State.RouteIndex,
            local.Add(host.Pos.X, host.Pos.Y, host.Pos.Z), api.World.ElapsedMilliseconds);
    }
    public double RenderOrder => .55;
    public int RenderRange => 48;
    internal static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    internal static double ChargeFrame(double reserve)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++) { double mid = (lo + hi) / 2; if (Smooth(mid) < reserve) lo = mid; else hi = mid; }
        return (lo + hi) * 50;
    }
    internal static float[] Orientation(BlockFacing input, BlockFacing output)
    {
        // Canonical straight: west->east. Canonical elbow: west->up.
        if (input.Opposite == output)
            return PumpOrientation.Matrix(output, output.IsHorizontal ? BlockFacing.UP : BlockFacing.NORTH);
        return PumpOrientation.Matrix(input.Opposite, output);
    }

    private void Prepare(string kind, int connections = 15)
    {
        foreach (var b in bones) { b.Uploaded?.Dispose(); b.UploadedGlass?.Dispose(); }
        bones = new() { new Bone() };
        shape = Shape.TryGet(api, "gearwright:shapes/block/pneumatic-" + kind + ".json");
        frames = shape.Animations?.FirstOrDefault()?.KeyFrames ?? Array.Empty<AnimationKeyFrame>();
        var moving = frames.SelectMany(f => f.Elements.Keys).ToHashSet();
        if (kind == "router")
        {
            for (int n = 1; n <= 4; n++) moving.Add($"port-{n}-valve-pivot");
            void RoleParts(ShapeElement e)
            {
                if (e.Name?.Contains("-scoop-") == true || e.Name?.Contains("-flow-chevron-") == true) moving.Add(e.Name);
                foreach (var child in e.Children ?? Array.Empty<ShapeElement>()) RoleParts(child);
            }
            foreach (var e in shape.Elements) RoleParts(e);
        }
        var textures = new Textures(api, shape);
        void Visit(ShapeElement e, int bone, float[] ancestor)
        {
            if (kind == "router") for (int n = 1; n <= 4; n++)
                if (e.Name == $"port-{n}" && (connections & (1 << (n - 1))) == 0 ||
                    e.Name == $"router-cap-{n}" && (connections & (1 << (n - 1))) != 0) return;
            bool animate = e.Name != null && moving.Contains(e.Name);
            if (animate) { bones.Add(new Bone { Parent = bone, Element = e, Ancestors = ancestor }); bone = bones.Count - 1; }
            float[] transform = animate ? Mat4f.Create() : Mat4f.Mul(Mat4f.Create(), ancestor, Local(e, null, null, 0));
            if (e.HasFaces())
            {
                var part = e.Clone(); part.Children = null; part.ParentElement = null;
                part.To = part.To!.Zip(part.From!, (end, start) => end - start).ToArray();
                part.From = new double[3]; part.RotationOrigin = new double[3];
                part.RotationX = part.RotationY = part.RotationZ = 0;
                var section = new Shape { Elements = new[] { part }, TextureWidth = shape.TextureWidth,
                    TextureHeight = shape.TextureHeight, TextureSizes = shape.TextureSizes, Textures = shape.Textures };
                api.Tesselator.TesselateShape("gearwright-pneumatic", section, out MeshData mesh, textures);
                mesh.MatrixTransform(transform);
                (e.RenderPass == 1 ? bones[bone].Glass : bones[bone].Mesh).AddMeshData(mesh);
            }
            foreach (var child in e.Children ?? Array.Empty<ShapeElement>()) Visit(child, bone, transform);
        }
        foreach (var element in shape.Elements) Visit(element, 0, Mat4f.Create());
        foreach (var b in bones)
        {
            if (b.Mesh.VerticesCount > 0) b.Uploaded = api.Render.UploadMultiTextureMesh(b.Mesh);
            if (b.Glass.VerticesCount > 0) b.UploadedGlass = api.Render.UploadMultiTextureMesh(b.Glass);
            b.Mesh = b.Glass = null!;
        }
        prepared = kind == "router" ? kind + "|" + connections : kind;
    }

    internal static float[] Local(ShapeElement e, AnimationKeyFrameElement? a, AnimationKeyFrameElement? b, double mix)
    {
        double L(double? low, double? high) => (low ?? 0) * (1 - mix) + (high ?? 0) * mix;
        double[] o = e.RotationOrigin ?? new double[3];
        return new Matrixf().Identity().Translate(L(a?.OffsetX, b?.OffsetX) / 16, L(a?.OffsetY, b?.OffsetY) / 16, L(a?.OffsetZ, b?.OffsetZ) / 16)
            .Translate(o[0] / 16, o[1] / 16, o[2] / 16)
            .RotateZ((float)((e.RotationZ + L(a?.RotationZ, b?.RotationZ)) * GameMath.DEG2RAD))
            .RotateY((float)((e.RotationY + L(a?.RotationY, b?.RotationY)) * GameMath.DEG2RAD))
            .RotateX((float)((e.RotationX + L(a?.RotationX, b?.RotationX)) * GameMath.DEG2RAD))
            .Translate(-o[0] / 16, -o[1] / 16, -o[2] / 16)
            .Translate(e.From![0] / 16, e.From[1] / 16, e.From[2] / 16).Values;
    }

    internal static Vec3d CargoPosition(PneumaticState state, PneumaticLineKind kind, bool receiving, double? presented = null)
    {
        double progress = presented ?? state.Progress;
        double frame = progress * 360;
        if (state.Loading)
        {
            double lift = 6.2 * Smooth(frame / 150) * (1 - Smooth((frame - 250) / 110));
            return new Vec3d(frame <= 180 ? 8 : 8 + 10 * Smooth((frame - 180) / 60),
                1.78 + (frame <= 180 ? lift : 6.2), 8).Mul(1.0 / 16);
        }
        if (receiving)
        {
            double arrival = Math.Clamp((frame - 115) / 30, 0, 1), fall = Math.Clamp((frame - 145) / 45, 0, 1);
            double feed = Math.Max(0, (frame - 190) * Math.PI / 180 * 1.6);
            // The first short approach starts at the shared tube face, then
            // waits at the approved gate-open arrival position.
            double x = frame < 30 ? 2 * frame / 30 : 2 + 6 * arrival;
            return new Vec3d(x, 8 - (8 - 3.2) * fall - feed, 8).Mul(1.0 / 16);
        }
        var input = state.Input.Normali; var output = state.Output.Normali;
        double p = progress;
        if (p < .5) return new Vec3d(.5 + input.X * (.5 - p), .5 + input.Y * (.5 - p), .5 + input.Z * (.5 - p));
        // Complete insertion inside the dark sleeve before removing the mesh.
        // Ordinary host-to-host handoffs still meet at the shared block face.
        double distance = (p - .5) * (state.AtDeliveryOutlet ? 1 + 2.25 / 8 : 1);
        return new Vec3d(.5 + output.X * distance, .5 + output.Y * distance, .5 + output.Z * distance);
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        var host = entity as BlockEntityPneumaticTransport;
        var accumulator = entity as BlockEntityPneumaticAirIntake;
        string kind = accumulator != null ? "accumulator" : host!.Kind == PneumaticLineKind.Router ? "router" : host!.Kind == PneumaticLineKind.Sender ? "sender" :
            host.Kind == PneumaticLineKind.InlineReceiver ? "receiver" : host.State.Input.Opposite == host.State.Output ? "straight" : "elbow";
        if (host?.OutletChest != null) kind += "-terminal";
        int connections = host?.Kind == PneumaticLineKind.Router ? host.ConnectionMask : 15;
        if (prepared != (kind == "router" ? kind + "|" + connections : kind)) Prepare(kind, connections);
        bool preparingRouter = host?.Kind == PneumaticLineKind.Router && host.State.Cargo == null;
        double authoritative = accumulator != null ? accumulator.StoredAir / PneumaticAir.PlenumCapacity : preparingRouter ? host!.Router.Preparation :
            host!.State.Returning ? host.State.ReturnProgress : host.State.Progress;
        string key = accumulator != null ? "reserve" : preparingRouter ? "prepare-" + host!.Router.ExpectedParcel + "-" + host.Router.ReadyInput : host!.State.Returning ? "return" : host.State.Parcel;
        if (key != presentationKey)
        { shownProgress = priorProgress = targetProgress = authoritative; interpolationAge = 0; presentationKey = key; }
        if (targetProgress != authoritative)
        { priorProgress = shownProgress; targetProgress = authoritative; interpolationAge = 0; }
        interpolationAge += Math.Clamp(dt, 0, .1);
        shownProgress = priorProgress + (targetProgress - priorProgress) * Math.Min(1, interpolationAge / .1);
        double frame = accumulator != null ? ChargeFrame(shownProgress) :
            host!.State.Returning || host.State.Loading || host.IsReceiving && host.State.Cargo != null ? shownProgress * 360 : 0;
        AnimationKeyFrame? low = null, high = null;
        if (frames.Length > 0)
        {
            low = frames[0]; high = frames[^1];
            foreach (var f in frames) { if (f.Frame <= frame) low = f; if (f.Frame >= frame) { high = f; break; } }
        }
        double mix = high != null && low != null && high.Frame > low.Frame ? (frame - low.Frame) / (high.Frame - low.Frame) : 0;
        var camera = api.World.Player.Entity.CameraPos;
        var basis = new Matrixf().Identity().Translate(entity.Pos.X - camera.X, entity.Pos.Y - camera.Y, entity.Pos.Z - camera.Z);
        float[] orientation = accumulator != null ? PumpOrientation.Matrix(accumulator.Outlet.IsHorizontal ? accumulator.Outlet : BlockFacing.EAST, BlockFacing.UP) :
            host!.Kind == PneumaticLineKind.Router ? host.Router.Matrix :
            host.Kind is PneumaticLineKind.Sender or PneumaticLineKind.InlineReceiver ? PneumaticPlacement.EndpointMatrix(host.State.Output, host.State.InventoryFace) :
            Orientation(host!.State.Input, host.State.Output);
        Mat4f.Multiply(basis.Values, basis.Values, orientation);
        var shader = api.Render.PreparedStandardShader(entity.Pos.X, entity.Pos.Y, entity.Pos.Z, new Vec4f(1, 1, 1, 1));
        shader.ViewMatrix = api.Render.CameraMatrixOriginf; shader.ProjectionMatrix = api.Render.CurrentProjectionMatrix; shader.AlphaTest = .02f;
        api.Render.GlToggleBlend(false, EnumBlendMode.Standard);
        api.Render.GlDisableCullFace();
        try
        {
            var mechanical = routerPresentation.At(api.World.ElapsedMilliseconds);
            foreach (var bone in bones)
            {
                float[] parent = bone.Parent < 0 ? basis.Values : bones[bone.Parent].World;
                Mat4f.Multiply(bone.World, parent, bone.Ancestors);
                if (bone.Element != null)
                {
                    var routerPose = host?.Kind == PneumaticLineKind.Router ? PneumaticRouterMotion.Bone(host, bone.Element.Name!, shownProgress, mechanical) : null;
                    Mat4f.Multiply(bone.World, bone.World, Local(bone.Element,
                        routerPose ?? low?.Elements.GetValueOrDefault(bone.Element.Name!), routerPose ?? high?.Elements.GetValueOrDefault(bone.Element.Name!), mix));
                }
                shader.ModelMatrix = bone.World;
                bool alert = host?.Kind == PneumaticLineKind.Router && host.Router.Lost && host.State.Cargo != null && bone.Element?.Name == "door-in-temporal";
                float brightness = alert ? PneumaticRouterMotion.AlertBrightness(api.World.ElapsedMilliseconds / 1000.0) : 1;
                shader.RgbaTint = new Vec4f(brightness, brightness, brightness, 1);
                shader.ExtraGlow = alert ? (int)(Math.Min(1, brightness) * 255) : 0;
                shader.RgbaGlowIn = new Vec4f(.16f * brightness, .95f * brightness, .72f * brightness, alert ? .65f : 0);
                if (bone.Uploaded != null) api.Render.RenderMultiTextureMesh(bone.Uploaded, "tex");
            }
            shader.RgbaTint = new Vec4f(1, 1, 1, 1); shader.ExtraGlow = 0; shader.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
            if (host?.State.Cargo is { } cargo && cargoMotion.TryPosition(host.State.Parcel, host.State.Instance,
                api.World.ElapsedMilliseconds, out var cargoPosition))
            {
                var model = host.Kind == PneumaticLineKind.Router ? new Matrixf(host.Router.Matrix) : host.State.Loading || host.IsReceiving ?
                    new Matrixf(PneumaticPlacement.EndpointMatrix(host.State.Output, host.State.InventoryFace)) : new Matrixf().Identity();
                if (host.Kind == PneumaticLineKind.Router) model.RotateY((float)(mechanical.Angle * GameMath.DEG2RAD));
                model.Values[12] = (float)(cargoPosition.X - camera.X);
                model.Values[13] = (float)(cargoPosition.Y - camera.Y);
                model.Values[14] = (float)(cargoPosition.Z - camera.Z);
                model.Scale(.15f, .15f, .15f).Translate(-.5f, -.5f, -.5f);
                var info = api.Render.GetItemStackRenderInfo(new DummySlot(cargo), EnumItemRenderTarget.Ground, dt);
                shader.ModelMatrix = model.Values;
                api.Render.RenderMultiTextureMesh(info.ModelRef, "tex");
            }
            // Glass follows the real item draw and does not write depth. Its
            // transparent pixels cannot hide the cargo behind the front pane.
            api.Render.GlToggleBlend(true, EnumBlendMode.Standard);
            api.Render.GLDepthMask(false);
            foreach (var bone in bones)
                if (bone.UploadedGlass != null)
                { shader.ModelMatrix = bone.World; api.Render.RenderMultiTextureMesh(bone.UploadedGlass, "tex"); }
        }
        finally { api.Render.GLDepthMask(true); api.Render.GlToggleBlend(false, EnumBlendMode.Standard); shader.Stop(); api.Render.GlEnableCullFace(); }
        if (host?.Kind == PneumaticLineKind.Router && host.SelectedRouterPort > 0)
        {
            var normal = host.Router.Face(host.SelectedRouterPort).Normali;
            var center = new Vec3f(.5f + normal.X * .47f, .5f + normal.Y * .47f, .5f + normal.Z * .47f);
            var axes = Enumerable.Range(0, 3).Where(a => (a == 0 ? normal.X : a == 1 ? normal.Y : normal.Z) == 0).ToArray();
            var points = new float[4][];
            for (int i = 0; i < 4; i++) { points[i] = new[] { center.X, center.Y, center.Z }; points[i][axes[0]] += i == 0 || i == 3 ? -.23f : .23f; points[i][axes[1]] += i < 2 ? -.23f : .23f; }
            for (int i = 0; i < 4; i++) { var a = points[i]; var b = points[(i + 1) % 4]; api.Render.RenderLine(host.Pos, a[0], a[1], a[2], b[0], b[1], b[2], unchecked((int)0xffffce5a)); }
        }
    }

    private sealed class Textures : ITexPositionSource
    {
        private readonly Dictionary<string, TextureAtlasPosition> positions = new();
        public Size2i AtlasSize { get; }
        public Textures(ICoreClientAPI api, Shape shape)
        {
            AtlasSize = api.BlockTextureAtlas.Size;
            foreach (var pair in shape.Textures)
            {
                if (!api.BlockTextureAtlas.GetOrInsertTexture(pair.Value, out _, out var position))
                    throw new InvalidOperationException("Missing pneumatic texture: " + pair.Value);
                positions[pair.Key] = position;
            }
        }
        public TextureAtlasPosition this[string name] => positions[name.TrimStart('#')];
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        airSpecks.Release(entity);
        if (entity is BlockEntityPneumaticTransport host) cargoMotion.Release(host.State.Instance);
        foreach (var b in bones) { b.Uploaded?.Dispose(); b.UploadedGlass?.Dispose(); }
        bones.Clear();
    }
}
