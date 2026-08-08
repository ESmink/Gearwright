using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Renders the pressure-driven rotor and visible fluid without storing liquid in the pipe.</summary>
internal sealed class HydraulicPipeRenderer : IRenderer, IDisposable
{
    private readonly BlockEntityFluidPipe pipe;
    private readonly ICoreClientAPI capi;
    private readonly Random speedRandom;
    private readonly MeshRef rotorMesh;
    private readonly Matrixf modelMatrix = new();
    private ScrollingLiquidSurface? fluidSurface;
    private string? fluidMeshCode;
    private int fluidColor = ColorUtil.ToRgba(220, 66, 132, 220);
    private float rotorAngle;
    private float particleAccumulator;
    private float fluidPhase;
    private float speedVariation = 1;
    private float targetSpeedVariation = 1;
    private float variationSecondsRemaining;

    public HydraulicPipeRenderer(BlockEntityFluidPipe pipe, ICoreClientAPI capi)
    {
        this.pipe = pipe;
        this.capi = capi;
        speedRandom = new Random(unchecked(pipe.Pos.X * 73856093 ^ pipe.Pos.Y * 19349663 ^ pipe.Pos.Z * 83492791));
        PickNextSpeedVariation();
        MeshData rotor = HydraulicPipeMesh.Tesselate(pipe, capi.Tesselator, "gearwright:shapes/block/sprinkler-rotor.json");
        rotorMesh = capi.Render.UploadMesh(rotor);
    }

    public double RenderOrder => 0.5;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        double pressure = pipe.CurrentPressure;
        float performance = (float)HydraulicMath.Performance(pressure);
        if (pipe.HasSprinkler)
        {
            if (performance > 0)
            {
                float pressureCurve = MathF.Pow(performance, 1.6f);
                rotorAngle = (rotorAngle + deltaTime * (0.12f + 9.88f * pressureCurve)) % GameMath.TWOPI;
            }
            RenderMesh(rotorMesh, rotorAngle, 0, 1, capi.BlockTextureAtlas.AtlasTextures[0].TextureId);
            if (performance > 0)
            {
                if (pipe.CurrentLiquidCode != null) EnsureFluidMesh(pipe.CurrentLiquidCode);
                SpawnSpray(deltaTime, performance);
            }
        }

        if (pressure <= 0 || pipe.CurrentLiquidCode == null) return;
        if (!HasWindow()) return;
        EnsureFluidMesh(pipe.CurrentLiquidCode);
        if (fluidSurface == null) return;
        float pressureResponse = MathF.Pow(performance, 2.2f);
        float pixelsPerSecond = (0.04f + 47.96f * pressureResponse) * UpdateSpeedVariation(deltaTime);
        fluidPhase = (fluidPhase + deltaTime * pixelsPerSecond / 16f) % 1;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.GetAddon(face) != HydraulicFaceAddon.GlassWindow) continue;
            WindowScroll scroll = GetWindowScroll(face);
            fluidSurface.Update(fluidPhase, scroll.Horizontal, !scroll.Reverse);
            RenderMesh(fluidSurface.MeshRef, FaceYaw(face), FacePitch(face), 0.70f, fluidSurface.TextureId);
        }
    }

    private void RenderMesh(
        MeshRef mesh,
        float yawOrRotor,
        float pitch,
        float opacity,
        int textureId)
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity()
            .Translate(pipe.Pos.X - camera.X, pipe.Pos.Y - camera.Y, pipe.Pos.Z - camera.Z)
            .Translate(0.5f, 0.5f, 0.5f);
        if (pitch != 0) modelMatrix.RotateX(pitch);
        if (yawOrRotor != 0) modelMatrix.RotateY(yawOrRotor);
        modelMatrix.Translate(-0.5f, -0.5f, -0.5f);

        bool blended = opacity < 0.999f;
        if (blended) capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pipe.Pos.X, pipe.Pos.Y, pipe.Pos.Z, new Vec4f(1, 1, 1, opacity));
        shader.Tex2D = textureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = 0.05f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
        if (blended) capi.Render.GlToggleBlend(false);
    }

    private float UpdateSpeedVariation(float deltaTime)
    {
        variationSecondsRemaining -= deltaTime;
        if (variationSecondsRemaining <= 0) PickNextSpeedVariation();
        float response = Math.Min(1, deltaTime * 0.65f);
        speedVariation += (targetSpeedVariation - speedVariation) * response;
        return speedVariation;
    }

    private void PickNextSpeedVariation()
    {
        targetSpeedVariation = 0.9f + (float)speedRandom.NextDouble() * 0.2f;
        variationSecondsRemaining = 2.5f + (float)speedRandom.NextDouble() * 4.5f;
    }

    private void EnsureFluidMesh(AssetLocation liquidCode)
    {
        string code = liquidCode.ToString();
        if (fluidMeshCode == code && fluidSurface != null) return;
        fluidSurface?.Dispose();
        fluidSurface = null;
        fluidMeshCode = code;

        Item? item = capi.World.GetItem(liquidCode);
        WaterTightContainableProps? props = item?.Attributes?["waterTightContainerProps"]
            .AsObject<WaterTightContainableProps>(null, liquidCode.Domain);
        if (props?.Texture == null) return;

        try
        {
            ContainerTextureSource textureSource = new(capi, new ItemStack(item), props.Texture);
            TextureAtlasPosition position = textureSource["liquid"];
            if (position.AvgColor != 0)
            {
                fluidColor = ColorUtil.ToRgba(
                    220,
                    ColorUtil.ColorR(position.AvgColor),
                    ColorUtil.ColorG(position.AvgColor),
                    ColorUtil.ColorB(position.AvgColor));
            }
            fluidSurface = new ScrollingLiquidSurface(
                capi, position,
                6.5f / 16f, 6.5f / 16f,
                9.5f / 16f, 9.5f / 16f,
                6.08f / 16f);
        }
        catch (Exception exception)
        {
            capi.Logger.Error(
                "[Gearwright] Could not prepare the visible pipe liquid texture for {0}: {1}",
                liquidCode, exception.Message);
        }
    }

    private void SpawnSpray(float deltaTime, float performance)
    {
        particleAccumulator += deltaTime * (60 + 240 * performance);
        int quantity = (int)particleAccumulator;
        if (quantity <= 0) return;
        particleAccumulator -= quantity;

        int[] jetCounts = new int[4];
        for (int particle = 0; particle < quantity; particle++)
        {
            jetCounts[particle & 3]++;
        }

        float outward = 0.8f + 1.7f * performance;
        float spread = 0.18f + 0.42f * performance;
        float liftMin = 0.45f + 0.65f * performance;
        float liftMax = 0.9f + 1.5f * performance;
        float life = 0.75f + 0.85f * performance;
        float scale = 0.13f + 0.09f * performance;
        Vec3d origin = pipe.Pos.ToVec3d();

        SpawnJet(jetCounts[0], origin.AddCopy(0.5, 0.045, 0.18),
            new Vec3f(-spread, liftMin, -outward - spread),
            new Vec3f(spread, liftMax, -outward + spread), life, scale);
        SpawnJet(jetCounts[1], origin.AddCopy(0.5, 0.045, 0.82),
            new Vec3f(-spread, liftMin, outward - spread),
            new Vec3f(spread, liftMax, outward + spread), life, scale);
        SpawnJet(jetCounts[2], origin.AddCopy(0.18, 0.045, 0.5),
            new Vec3f(-outward - spread, liftMin, -spread),
            new Vec3f(-outward + spread, liftMax, spread), life, scale);
        SpawnJet(jetCounts[3], origin.AddCopy(0.82, 0.045, 0.5),
            new Vec3f(outward - spread, liftMin, -spread),
            new Vec3f(outward + spread, liftMax, spread), life, scale);
    }

    private void SpawnJet(
        int quantity,
        Vec3d position,
        Vec3f minVelocity,
        Vec3f maxVelocity,
        float life,
        float scale)
    {
        if (quantity <= 0) return;
        Vec3d minPos = position.AddCopy(-0.025, -0.012, -0.025);
        Vec3d maxPos = position.AddCopy(0.025, 0.012, 0.025);
        capi.World.SpawnParticles(
            quantity,
            fluidColor,
            minPos,
            maxPos,
            minVelocity,
            maxVelocity,
            life,
            1f,
            scale,
            EnumParticleModel.Cube,
            null);
    }

    private bool HasWindow()
    {
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.GetAddon(face) == HydraulicFaceAddon.GlassWindow) return true;
        }
        return false;
    }

    private WindowScroll GetWindowScroll(BlockFacing windowFace)
    {
        BlockFacing? direction = pipe.CurrentFlowDirection;

        if (direction == null)
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (pipe.IsConnected(face))
                {
                    direction = face;
                    break;
                }
            }
        }
        if (direction == null || direction.Axis == windowFace.Axis)
        {
            // End-on windows cannot project depth onto their pane, so use a stable
            // vertical scroll instead of translating the liquid toward the camera.
            return new WindowScroll(false, direction == windowFace.Opposite);
        }

        if (windowFace == BlockFacing.NORTH)
        {
            if (direction == BlockFacing.EAST || direction == BlockFacing.WEST)
                return new WindowScroll(true, direction == BlockFacing.WEST);
            return new WindowScroll(false, direction == BlockFacing.UP);
        }
        if (windowFace == BlockFacing.SOUTH)
        {
            if (direction == BlockFacing.EAST || direction == BlockFacing.WEST)
                return new WindowScroll(true, direction == BlockFacing.EAST);
            return new WindowScroll(false, direction == BlockFacing.UP);
        }
        if (windowFace == BlockFacing.EAST)
        {
            if (direction == BlockFacing.NORTH || direction == BlockFacing.SOUTH)
                return new WindowScroll(true, direction == BlockFacing.NORTH);
            return new WindowScroll(false, direction == BlockFacing.UP);
        }
        if (windowFace == BlockFacing.WEST)
        {
            if (direction == BlockFacing.NORTH || direction == BlockFacing.SOUTH)
                return new WindowScroll(true, direction == BlockFacing.SOUTH);
            return new WindowScroll(false, direction == BlockFacing.UP);
        }
        if (windowFace == BlockFacing.UP)
        {
            if (direction == BlockFacing.EAST || direction == BlockFacing.WEST)
                return new WindowScroll(true, direction == BlockFacing.WEST);
            return new WindowScroll(false, direction == BlockFacing.NORTH);
        }

        if (direction == BlockFacing.EAST || direction == BlockFacing.WEST)
            return new WindowScroll(true, direction == BlockFacing.WEST);
        return new WindowScroll(false, direction == BlockFacing.SOUTH);
    }

    private static float FaceYaw(BlockFacing face)
    {
        if (face == BlockFacing.EAST) return -GameMath.PIHALF;
        if (face == BlockFacing.SOUTH) return GameMath.PI;
        if (face == BlockFacing.WEST) return GameMath.PIHALF;
        return 0;
    }

    private static float FacePitch(BlockFacing face)
    {
        if (face == BlockFacing.UP) return GameMath.PIHALF;
        if (face == BlockFacing.DOWN) return -GameMath.PIHALF;
        return 0;
    }

    public void Dispose()
    {
        rotorMesh.Dispose();
        fluidSurface?.Dispose();
        fluidSurface = null;
    }

    private readonly record struct WindowScroll(bool Horizontal, bool Reverse);
}
