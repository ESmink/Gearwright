using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>Draws both live shaft networks and phase-driven orbiting pawls.</summary>
internal sealed class OverrunningTransmissionRenderer : IRenderer, IDisposable
{
    private const float PawlMountRadius = 5.82f / 16f;
    private const float PawlPivotLead = .62f / 16f;
    private const float SpringAnchorY = .32f / 16f;
    private const float SpringAnchorZ = .12f / 16f;

    private static readonly float[] PawlAngles =
    {
        0,
        120f * GameMath.DEG2RAD,
        240f * GameMath.DEG2RAD
    };

    private readonly BEBehaviorMPOverrunningTransmission transmission;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef[] inputMeshes;
    private readonly MeshRef[] outputMeshes;
    private readonly MeshRef[][] pawlMeshes;
    private readonly MeshRef[][] springMeshes;
    private readonly Matrixf modelMatrix = new();
    private OverrunningRotorState lastState;
    private int handedness = 1;
    private bool overrunPose;

    public OverrunningTransmissionRenderer(
        BEBehaviorMPOverrunningTransmission transmission,
        ICoreClientAPI capi)
    {
        this.transmission = transmission;
        this.capi = capi;
        inputMeshes = UploadPair("overrunning-transmission-input");
        outputMeshes = UploadPair("overrunning-transmission-output");
        pawlMeshes = new MeshRef[2][];
        springMeshes = new MeshRef[2][];
        for (int hand = 0; hand < 2; hand++)
        {
            string suffix = hand == 0 ? "" : "-reverse";
            pawlMeshes[hand] = new MeshRef[3];
            springMeshes[hand] = new MeshRef[3];
            for (int index = 0; index < 3; index++)
            {
                int number = index + 1;
                pawlMeshes[hand][index] = Upload(
                    $"gearwright:shapes/block/overrunning-transmission-pawl-{number}{suffix}.json");
                springMeshes[hand][index] = Upload(
                    $"gearwright:shapes/block/overrunning-transmission-spring-{number}{suffix}.json");
            }
        }
    }

    public double RenderOrder => .5;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (transmission.TryGetRotorState(out OverrunningRotorState current))
        {
            lastState = current;
        }

        float direction = OverrunningCouplingMath.OperatingDirection(
            lastState.InputSpeed,
            lastState.OutputSpeed);
        if (Math.Abs(lastState.InputSpeed) > OverrunningCouplingMath.EngagementEpsilon ||
            Math.Abs(lastState.OutputSpeed) > OverrunningCouplingMath.EngagementEpsilon)
        {
            handedness = direction < 0 ? -1 : 1;
        }

        float directedInput = lastState.InputSpeed * handedness;
        float directedOutput = lastState.OutputSpeed * handedness;
        if (directedOutput - directedInput > OverrunningCouplingMath.ReleaseSpeedDifference)
        {
            overrunPose = true;
        }
        else if (directedInput - directedOutput >= OverrunningCouplingMath.EngageSpeedDifference)
        {
            overrunPose = false;
        }

        int handIndex = handedness < 0 ? 1 : 0;
        float yaw = FacingYaw(transmission.InputFace);
        RenderRotor(inputMeshes[handIndex], yaw, lastState.InputAngle);
        RenderRotor(outputMeshes[handIndex], yaw, lastState.OutputAngle);

        float relative = handedness * (lastState.OutputAngle - lastState.InputAngle);
        float lift = overrunPose ? OverrunningPawlMath.Lift(relative) : 0;
        for (int index = 0; index < PawlAngles.Length; index++)
        {
            MountPoint(PawlAngles[index], handedness, out float mountY, out float mountZ);
            RenderPart(
                pawlMeshes[handIndex][index],
                yaw,
                lastState.OutputAngle,
                mountY,
                mountZ,
                handedness * lift);

            SpringPoint(
                PawlAngles[index],
                handedness,
                mountY,
                mountZ,
                out float springY,
                out float springZ);
            RenderPart(
                springMeshes[handIndex][index],
                yaw,
                lastState.OutputAngle,
                springY,
                springZ,
                handedness * lift * OverrunningPawlMath.SpringLiftFraction);
        }
    }

    private MeshRef[] UploadPair(string stem)
    {
        return new[]
        {
            Upload($"gearwright:shapes/block/{stem}.json"),
            Upload($"gearwright:shapes/block/{stem}-reverse.json")
        };
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

    private void RenderPart(
        MeshRef mesh,
        float yaw,
        float outputAngle,
        float pivotY,
        float pivotZ,
        float lift)
    {
        BeginModel(yaw)
            .RotateX(outputAngle)
            .Translate(0, pivotY, pivotZ)
            .RotateX(lift)
            .Translate(0, -pivotY, -pivotZ)
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

    private static void MountPoint(
        float angle,
        int hand,
        out float y,
        out float z)
    {
        y = MathF.Cos(angle) * PawlMountRadius - MathF.Sin(angle) * PawlPivotLead;
        z = hand * (
            MathF.Sin(angle) * PawlMountRadius + MathF.Cos(angle) * PawlPivotLead);
    }

    private static void SpringPoint(
        float angle,
        int hand,
        float mountY,
        float mountZ,
        out float y,
        out float z)
    {
        y = mountY + MathF.Cos(angle) * SpringAnchorY - MathF.Sin(angle) * SpringAnchorZ;
        z = mountZ + hand * (
            MathF.Sin(angle) * SpringAnchorY + MathF.Cos(angle) * SpringAnchorZ);
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
        foreach (MeshRef mesh in inputMeshes) mesh.Dispose();
        foreach (MeshRef mesh in outputMeshes) mesh.Dispose();
        foreach (MeshRef[] hand in pawlMeshes)
        {
            foreach (MeshRef mesh in hand) mesh.Dispose();
        }
        foreach (MeshRef[] hand in springMeshes)
        {
            foreach (MeshRef mesh in hand) mesh.Dispose();
        }
    }
}
