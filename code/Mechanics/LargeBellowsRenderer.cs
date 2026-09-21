using System;
using System.Collections.Generic;
using System.Linq;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Meshes static sections once and poses their hinges from the actual journal.</summary>
internal sealed class LargeBellowsRenderer : IRenderer
{
    private readonly BEBehaviorLargeBellows bellows;
    private readonly ICoreClientAPI capi;
    private List<Bone>? body, bottom, top;
    private readonly Dictionary<string, Motion> poses = new();
    private static readonly HashSet<string> Moving = new()
    {
        "BottomPlatePIVOT", "Fold1-PIVOTlower", "Fold2-PIVOT", "Fold3-PIVOT", "Fold4-PIVOT-top",
        "Fold5-PIVOT-top", "Fold6-PIVOT-top", "TopPlate-PIVOT", "FLAP", "gw-bellows-rod",
        "gw-bellows-rocker", "gw-bellows-plate-pin", "gw-bellows-lift-front", "gw-bellows-lift-back"
    };
    private readonly record struct Motion(double Angle = 0, double X = 0, double Y = 0, double Z = 0);
    private sealed class Bone
    {
        public int Parent = -1;
        public ShapeElement? Element;
        public float[] Ancestors = Mat4f.Create();
        public float[] World = Mat4f.Create();
        public MeshData Mesh = new(4, 6);
        public MultiTextureMeshRef? Uploaded;
    }
    public LargeBellowsRenderer(BEBehaviorLargeBellows bellows, ICoreClientAPI capi)
    { this.bellows = bellows; this.capi = capi; }
    public double RenderOrder => .55;
    public int RenderRange => 48;

    private static Matrixf Local(ShapeElement e, Motion pose) => ElementTransform(e, pose.Angle, pose.X, pose.Y, pose.Z);

    internal static Matrixf ElementTransform(ShapeElement e, double angle = 0, double x = 0, double y = 0, double z = 0)
    {
        double[] o = e.RotationOrigin ?? new double[3];
        return new Matrixf().Identity().Translate(x / 16, y / 16, z / 16)
            .Translate(o[0] / 16, o[1] / 16, o[2] / 16)
            .RotateZ((float)(e.RotationZ * GameMath.DEG2RAD + angle))
            .RotateY((float)(e.RotationY * GameMath.DEG2RAD)).RotateX((float)(e.RotationX * GameMath.DEG2RAD))
            .Translate(-o[0] / 16, -o[1] / 16, -o[2] / 16)
            .Translate(e.From![0] / 16, e.From[1] / 16, e.From[2] / 16);
    }

    private List<Bone> Prepare(Shape shape)
    {
        List<Bone> bones = new() { new Bone() };
        var textures = new TextureSource(capi, shape);
        void Visit(ShapeElement element, int bone, float[] ancestor)
        {
            if (element.Name == "LINKAGE-1d") return;
            bool moving = element.Name != null && Moving.Contains(element.Name);
            if (moving)
            {
                bones.Add(new Bone { Parent = bone, Element = element, Ancestors = ancestor });
                bone = bones.Count - 1;
            }
            float[] transform = moving ? Mat4f.Create() : Mat4f.Mul(Mat4f.Create(), ancestor, Local(element, default).Values);
            if (element.HasFaces())
            {
                ShapeElement part = element.Clone();
                part.Children = null; part.ParentElement = null;
                part.To = part.To!.Zip(part.From!, (end, start) => end - start).ToArray();
                part.From = new double[3]; part.RotationOrigin = new double[3];
                part.RotationX = part.RotationY = part.RotationZ = 0;
                var section = new Shape { Elements = new[] { part }, TextureWidth = shape.TextureWidth,
                    TextureHeight = shape.TextureHeight, TextureSizes = shape.TextureSizes, Textures = shape.Textures };
                capi.Tesselator.TesselateShape("gearwright-large-bellows", section, out MeshData mesh, textures);
                mesh.MatrixTransform(transform);
                bones[bone].Mesh.AddMeshData(mesh);
            }
            foreach (ShapeElement child in element.Children ?? Array.Empty<ShapeElement>()) Visit(child, bone, transform);
        }
        foreach (ShapeElement element in shape.Elements) Visit(element, 0, Mat4f.Create());
        foreach (Bone bone in bones)
        {
            if (bone.Mesh.VerticesCount > 0) bone.Uploaded = capi.Render.UploadMultiTextureMesh(bone.Mesh);
            bone.Mesh = null!;
        }
        return bones;
    }

    private static Motion Bar(BellowsPoint low, BellowsPoint high, double z = 0) =>
        new(Math.Atan2(low.X - high.X, high.Y - low.Y), (low.X + high.X) / 2, (low.Y + high.Y) / 2, z);

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        var drive = bellows.SelectedDrive();
        if (drive == null) return;
        body ??= Prepare(Shape.TryGet(capi, "game:shapes/block/wood/mechanics/bellowslarge.json").Clone());
        bool upper = bellows.DriveFace == BlockFacing.UP;
        if (upper) top ??= Prepare(Shape.TryGet(capi, "gearwright:shapes/block/large-bellows-top.json"));
        else bottom ??= Prepare(Shape.TryGet(capi, "gearwright:shapes/block/large-bellows-bottom.json"));
        LargeBellowsSystem.HideLegacyAnimation(bellows.Blockentity);
        double phase = LargeBellowsMotion.Phase(drive.DeviceAngle(bellows), upper);
        BellowsPose p = LargeBellowsMotion.Pose(phase, upper);
        double fill = upper ? p.UpperFill : bellows.ReservoirFill;
        double compression = (p.BottomAngle * GameMath.RAD2DEG + 12) / 10.5;
        poses.Clear();
        void Rotate(string name, double degrees) => poses[name] = new(degrees * GameMath.DEG2RAD);
        Rotate("BottomPlatePIVOT", p.BottomAngle * GameMath.RAD2DEG + 12);
        Rotate("Fold1-PIVOTlower", 1.65 * compression); Rotate("Fold2-PIVOT", 3.4 * compression);
        Rotate("Fold3-PIVOT", 3.4 * compression); Rotate("Fold4-PIVOT-top", 1.5 * fill);
        Rotate("Fold5-PIVOT-top", 4 * fill); Rotate("Fold6-PIVOT-top", 4 * fill); Rotate("TopPlate-PIVOT", 2 * fill);
        double travel = -drive.DeviceTravel(bellows, (drive.Network?.Speed ?? 0) * .01);
        Rotate("FLAP", LargeBellowsMotion.IsTakingInAir(phase, travel, upper) ? 25 : 0);
        poses["gw-bellows-rod"] = Bar(p.Journal, p.Input, drive.DeviceOffset(bellows) * 16);
        poses["gw-bellows-rocker"] = new(p.RockerAngle);
        poses["gw-bellows-plate-pin"] = new(upper ? 11.5 * fill * GameMath.DEG2RAD : p.BottomAngle, p.Plate.X, p.Plate.Y);
        foreach (string side in new[] { "front", "back" })
            poses["gw-bellows-lift-" + side] = upper ? Bar(p.Plate, p.Output) : Bar(p.Output, p.Plate);

        Vec3d camera = capi.World.Player.Entity.CameraPos;
        Matrixf basis = new Matrixf().Identity().Translate(bellows.Pos.X - camera.X, bellows.Pos.Y - camera.Y, bellows.Pos.Z - camera.Z);
        Mat4f.Multiply(basis.Values, basis.Values, PumpOrientation.Matrix(bellows.NozzleFace.Opposite, BlockFacing.UP));
        var shader = capi.Render.PreparedStandardShader(bellows.Pos.X, bellows.Pos.Y, bellows.Pos.Z, new Vec4f(1, 1, 1, 1));
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf; shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;
        void Draw(List<Bone> bones)
        {
            foreach (Bone bone in bones)
            {
                float[] parent = bone.Parent < 0 ? basis.Values : bones[bone.Parent].World;
                Mat4f.Multiply(bone.World, parent, bone.Ancestors);
                if (bone.Element != null)
                    Mat4f.Multiply(bone.World, bone.World, Local(bone.Element, poses.GetValueOrDefault(bone.Element.Name!)).Values);
                shader.ModelMatrix = bone.World;
                // The standard shader uses "tex" (also used by its Tex2D setter).
                if (bone.Uploaded != null) capi.Render.RenderMultiTextureMesh(bone.Uploaded, "tex");
            }
        }
        try { Draw(body); Draw(upper ? top! : bottom!); }
        finally { shader.Stop(); }
    }

    private sealed class TextureSource : ITexPositionSource
    {
        private readonly Dictionary<string, TextureAtlasPosition> positions = new();
        public Size2i AtlasSize { get; }
        public TextureSource(ICoreClientAPI api, Shape shape)
        {
            AtlasSize = api.BlockTextureAtlas.Size;
            foreach (var pair in shape.Textures)
            {
                if (!api.BlockTextureAtlas.GetOrInsertTexture(pair.Value, out _, out var position))
                    throw new InvalidOperationException("Missing bellows texture: " + pair.Value);
                positions[pair.Key] = position;
            }
        }
        public TextureAtlasPosition this[string name] => positions[name.TrimStart('#')];
    }

    public void Dispose()
    {
        foreach (var bones in new[] { body, bottom, top })
            if (bones != null) foreach (Bone bone in bones) bone.Uploaded?.Dispose();
    }
}
