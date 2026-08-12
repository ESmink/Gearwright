using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Draws both live shaft networks and phase-driven orbiting pawls.</summary>
internal sealed class OverrunningTransmissionRenderer : IRenderer, IDisposable
{
    private const float PawlMountRadius = 5.15f / 16f;
    private const float ToothPitch = GameMath.PI / 6f;
    private const float MaximumPawlLift = 14f * GameMath.DEG2RAD;

    private static readonly float[] PawlAngles =
    {
        0,
        120f * GameMath.DEG2RAD,
        240f * GameMath.DEG2RAD
    };
    private static readonly float[] PawlPhases = { 0, 1f / 3f, 2f / 3f };

    private readonly BEBehaviorMPControlledTransmission transmission;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef inputMesh;
    private readonly MeshRef outputMesh;
    private readonly MeshRef[] pawlMeshes;
    private readonly Matrixf modelMatrix = new();
    private OverrunningRotorState lastState;

    public OverrunningTransmissionRenderer(
        BEBehaviorMPControlledTransmission transmission,
        ICoreClientAPI capi)
    {
        this.transmission = transmission;
        this.capi = capi;
        inputMesh = Upload("gearwright:shapes/block/overrunning-transmission-input.json");
        outputMesh = Upload("gearwright:shapes/block/overrunning-transmission-output.json");
        pawlMeshes = new[]
        {
            Upload("gearwright:shapes/block/overrunning-transmission-pawl-1.json"),
            Upload("gearwright:shapes/block/overrunning-transmission-pawl-2.json"),
            Upload("gearwright:shapes/block/overrunning-transmission-pawl-3.json")
        };
    }

    public double RenderOrder => .5;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (transmission.TryGetRotorState(out OverrunningRotorState current))
        {
            lastState = current;
        }

        float yaw = FacingYaw(transmission.OverrunningInputFace);
        RenderRotor(inputMesh, yaw, lastState.InputAngle);
        RenderRotor(outputMesh, yaw, lastState.OutputAngle);

        bool driving = lastState.InputSpeed - lastState.OutputSpeed >
            OverrunningCouplingMath.EngagementEpsilon;
        float relative = lastState.OutputAngle - lastState.InputAngle;
        for (int index = 0; index < pawlMeshes.Length; index++)
        {
            float lift = driving ? 0 : PawlLift(relative, PawlPhases[index]);
            RenderPawl(
                pawlMeshes[index],
                yaw,
                lastState.OutputAngle,
                PawlAngles[index],
                lift);
        }
    }

    private MeshRef Upload(string location)
    {
        Shape shape = Shape.TryGet(capi, location);
        capi.Tesselator.TesselateShape(
            transmission.Owner.Block,
            shape,
            out MeshData mesh,
            new Vec3f(),
            null,
            null);
        return capi.Render.UploadMesh(mesh);
    }

    private void RenderRotor(MeshRef mesh, float yaw, float angle)
    {
        BeginModel(yaw)
            .RotateX(angle)
            .Translate(-.5f, -.5f, -.5f);
        Render(mesh);
    }

    private void RenderPawl(
        MeshRef mesh,
        float yaw,
        float outputAngle,
        float mountAngle,
        float lift)
    {
        float mountY = MathF.Cos(mountAngle) * PawlMountRadius;
        float mountZ = MathF.Sin(mountAngle) * PawlMountRadius;
        BeginModel(yaw)
            .RotateX(outputAngle)
            .Translate(0, mountY, mountZ)
            .RotateX(lift)
            .Translate(0, -mountY, -mountZ)
            .Translate(-.5f, -.5f, -.5f);
        Render(mesh);
    }

    private Matrixf BeginModel(float yaw)
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        return modelMatrix.Identity()
            .Translate(
                transmission.Owner.Pos.X - camera.X,
                transmission.Owner.Pos.Y - camera.Y,
                transmission.Owner.Pos.Z - camera.Z)
            .Translate(.5f, .5f, .5f)
            .RotateY(yaw);
    }

    private void Render(MeshRef mesh)
    {
        BlockPos pos = transmission.Owner.Pos;
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pos.X, pos.Y, pos.Z, new Vec4f(1, 1, 1, 1));
        shader.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
    }

    internal static float PawlLift(float relativeAngle, float phase)
    {
        float cycle = PositiveModulo(relativeAngle / ToothPitch + phase, 1);
        float normalized = cycle < .7f
            ? cycle / .7f
            : (1 - cycle) / .3f;
        return Math.Max(0, normalized) * MaximumPawlLift;
    }

    private static float PositiveModulo(float value, float divisor)
    {
        float result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static float FacingYaw(BlockFacing input)
    {
        if (input == BlockFacing.NORTH) return -GameMath.PIHALF;
        if (input == BlockFacing.EAST) return GameMath.PI;
        if (input == BlockFacing.SOUTH) return GameMath.PIHALF;
        return 0;
    }

    public void Dispose()
    {
        inputMesh.Dispose();
        outputMesh.Dispose();
        foreach (MeshRef pawl in pawlMeshes) pawl.Dispose();
    }
}
