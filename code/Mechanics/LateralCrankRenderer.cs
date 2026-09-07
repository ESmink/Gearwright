using System;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

internal sealed class LateralCrankRenderer : IRenderer, IDisposable
{
    private readonly BEBehaviorMPLateralCrank crank;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef oneSidedMesh;
    private readonly MeshRef throughMesh;
    private readonly Matrixf modelMatrix = new();
    private BlockFacing shaftSide = BlockFacing.NORTH;
    private bool through;
    private bool topologyInvalid = true;

    public LateralCrankRenderer(BEBehaviorMPLateralCrank crank, ICoreClientAPI capi)
    {
        this.crank = crank;
        this.capi = capi;
        oneSidedMesh = Upload("lateral-crank-one-sided");
        throughMesh = Upload("lateral-crank-through");
    }

    public double RenderOrder => .5;
    public int RenderRange => 48;

    public void InvalidateTopology() => topologyInvalid = true;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        RefreshTopology();
        Render(through ? throughMesh : oneSidedMesh,
            (float)crank.AngleInFrame(shaftSide.Opposite, BlockFacing.UP));
    }

    private void RefreshTopology()
    {
        if (!topologyInvalid) return;
        topologyInvalid = false;
        (through, shaftSide) = VisualTopology();
    }

    private (bool Through, BlockFacing ShaftSide) VisualTopology()
    {
        BlockFacing[] faces = crank.AxisFaces();
        bool first = HasAxialConnection(faces[0]);
        bool second = HasAxialConnection(faces[1]);
        if (first && second) return (true, faces[0]);
        if (first) return (false, faces[0]);
        if (second) return (false, faces[1]);
        return (false, faces[0]);
    }

    private bool HasAxialConnection(BlockFacing face)
    {
        BlockPos position = crank.Position.AddCopy(face);
        Block block = capi.World.BlockAccessor.GetBlock(position);
        return block is Vintagestory.GameContent.Mechanics.IMechanicalPowerBlock mechanical &&
            mechanical.HasMechPowerConnectorAt(
                capi.World, position, face.Opposite,
                (Vintagestory.GameContent.Mechanics.BlockMPBase)crank.Blockentity.Block);
    }

    private MeshRef Upload(string shapeName)
    {
        Shape shape = Shape.TryGet(capi, $"gearwright:shapes/block/{shapeName}.json");
        capi.Tesselator.TesselateShape(
            crank.Blockentity.Block,
            shape,
            out MeshData mesh,
            new Vec3f(),
            null,
            null);
        return capi.Render.UploadMesh(mesh);
    }

    private void Render(MeshRef mesh, float angle)
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity().Translate(
            crank.Position.X - camera.X,
            crank.Position.Y - camera.Y,
            crank.Position.Z - camera.Z);
        Mat4f.Multiply(
            modelMatrix.Values,
            modelMatrix.Values,
            PumpOrientation.Matrix(shaftSide.Opposite, BlockFacing.UP));
        modelMatrix.Translate(.5f, .5f, .5f)
            .RotateX(angle)
            .Translate(-.5f, -.5f, -.5f);

        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            crank.Position.X,
            crank.Position.Y,
            crank.Position.Z,
            new Vec4f(1, 1, 1, 1));
        shader.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
    }

    public void Dispose()
    {
        oneSidedMesh.Dispose();
        throughMesh.Dispose();
    }
}
