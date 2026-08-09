using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Renders stored pipe contents, sprinkler motion, and pipe-nozzle particles.</summary>
internal sealed class HydraulicPipeRenderer : IRenderer, IDisposable
{
    private const float NozzleMouthOffset = 0.66f;
    private const float PipeMouthOffset = 0.52f;
    private const float IntakeConeLength = (1.05f - NozzleMouthOffset) * 0.5f;
    private const float IntakeTargetOffset = 0.62f;
    private const float ContentScrollSpeedMultiplier = 20f;
    private const float MaximumNozzleOutputSpeedMultiplier = 4f;
    private const float ParticleLightenFraction = 0.65f;
    private const int LiquidParticleAlpha = 104;
    private const int GasParticleAlpha = 72;
    private const int SprinklerParticleAlpha = 68;

    private readonly BlockEntityFluidPipe pipe;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef rotorMesh;
    private readonly Matrixf modelMatrix = new();
    private readonly double[] nozzleParticleAccumulators = new double[BlockFacing.NumberOfFaces];
    private PipeContentMesh? contentMesh;
    private string? contentMeshCode;
    private int contentTopologyMask = -1;
    private int contentColor = ColorUtil.ToRgba(LiquidParticleAlpha, 225, 235, 240);
    private int sprinklerColor = ColorUtil.ToRgba(SprinklerParticleAlpha, 225, 235, 240);
    private float rotorAngle;
    private float sprinklerParticleAccumulator;
    private int nextSprinklerNozzle;
    private float contentTexturePhase;
    private float displayedScrollSpeed;

    public HydraulicPipeRenderer(BlockEntityFluidPipe pipe, ICoreClientAPI capi)
    {
        this.pipe = pipe;
        this.capi = capi;
        MeshData rotor = HydraulicPipeMesh.Tesselate(
            pipe, capi.Tesselator, "gearwright:shapes/block/sprinkler-rotor.json");
        rotorMesh = capi.Render.UploadMesh(rotor);
    }

    public double RenderOrder => 0.5;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        AssetLocation? contentCode = pipe.CurrentContentCode;
        bool gas = contentCode != null && PipeContent.IsSteam(contentCode);
        bool hasContent = contentCode != null && pipe.ContentAmountLitres > 0;
        if (hasContent) EnsureContentMesh(contentCode!);
        float performance = gas ? 0 : (float)HydraulicMath.Performance(pipe.CurrentPressure);
        if (pipe.HasSprinkler)
        {
            if (performance > 0 && pipe.ContentAmountLitres > 0)
            {
                float pressureCurve = MathF.Pow(performance, 1.6f);
                rotorAngle = (rotorAngle + deltaTime * (0.12f + 9.88f * pressureCurve)) % GameMath.TWOPI;
            }
            RenderRotorMesh();
            if (performance > 0 && pipe.ContentAmountLitres > 0)
                SpawnSprinklerSpray(deltaTime, performance);
        }

        SpawnNozzleParticles(deltaTime, gas);
        if (!hasContent || !HasVisibleInterior()) return;
        if (contentMesh == null) return;
        PipeContentPhase phase = gas ? PipeContentPhase.Gas : PipeContentPhase.Liquid;
        float targetScrollSpeed = pipe.CurrentFlowDirection == null
            ? 0
            : (float)HydraulicMath.TextureScrollCyclesPerSecond(
                pipe.ThroughputLitresPerSecond, phase) * ContentScrollSpeedMultiplier;
        float response = 1 - MathF.Exp(-Math.Min(deltaTime, 0.25f) * 8f);
        displayedScrollSpeed += (targetScrollSpeed - displayedScrollSpeed) * response;
        if (pipe.CurrentFlowDirection != null && displayedScrollSpeed > 0.0001f)
        {
            contentTexturePhase = (contentTexturePhase + deltaTime * displayedScrollSpeed) % 1;
        }
        contentMesh.Update(
            pipe.FillFraction, gas, contentTexturePhase, pipe.CurrentFlowDirection);
        float opacity = gas
            ? 0.04f + 0.56f * (float)pipe.FillFraction
            : 0.70f;
        RenderMesh(contentMesh.MeshRef, opacity, contentMesh.TextureId);
    }

    private void EnsureContentMesh(AssetLocation contentCode)
    {
        string code = contentCode.ToString();
        int topology = PipeContentMesh.TopologyMask(pipe);
        if (contentMesh != null && contentMeshCode == code && contentTopologyMask == topology) return;
        contentMesh?.Dispose();
        contentMesh = null;
        contentMeshCode = code;
        contentTopologyMask = topology;

        bool gas = PipeContent.IsSteam(contentCode);
        contentColor = gas
            ? ColorUtil.ToRgba(GasParticleAlpha, 238, 242, 245)
            : ColorUtil.ToRgba(LiquidParticleAlpha, 225, 235, 240);
        sprinklerColor = ColorUtil.ToRgba(SprinklerParticleAlpha, 225, 235, 240);

        TextureAtlasPosition? position = null;
        if (gas)
        {
            if (capi.BlockTextureAtlas.GetOrInsertTexture(
                    PipeContent.SteamTexture, out _, out TextureAtlasPosition steamPosition))
                position = steamPosition;
        }
        else
        {
            Item? item = capi.World.GetItem(contentCode);
            WaterTightContainableProps? props = item?.Attributes?["waterTightContainerProps"]
                .AsObject<WaterTightContainableProps>(null, contentCode.Domain);
            if (item != null && props?.Texture != null)
            {
                try
                {
                    ContainerTextureSource textureSource = new(capi, new ItemStack(item), props.Texture);
                    position = textureSource["liquid"];
                }
                catch (Exception exception)
                {
                    capi.Logger.Error(
                        "[Gearwright] Could not prepare pipe content texture for {0}: {1}",
                        contentCode, exception.Message);
                }
            }
        }
        if (position == null) return;
        if (position.AvgColor != 0)
        {
            contentColor = BrightenedParticleColor(
                gas ? GasParticleAlpha : LiquidParticleAlpha,
                ColorUtil.ColorR(position.AvgColor),
                ColorUtil.ColorG(position.AvgColor),
                ColorUtil.ColorB(position.AvgColor));
            if (!gas)
            {
                sprinklerColor = BrightenedParticleColor(
                    SprinklerParticleAlpha,
                    ColorUtil.ColorR(position.AvgColor),
                    ColorUtil.ColorG(position.AvgColor),
                    ColorUtil.ColorB(position.AvgColor));
            }
        }
        contentMesh = new PipeContentMesh(capi, position, pipe);
    }

    private void RenderMesh(MeshRef mesh, float opacity, int textureId)
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity().Translate(
            pipe.Pos.X - camera.X, pipe.Pos.Y - camera.Y, pipe.Pos.Z - camera.Z);
        bool blended = opacity < 0.999f;
        if (blended) capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pipe.Pos.X, pipe.Pos.Y, pipe.Pos.Z, new Vec4f(1, 1, 1, opacity));
        shader.Tex2D = textureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = 0.02f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
        if (blended) capi.Render.GlToggleBlend(false);
    }

    private void RenderRotorMesh()
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity()
            .Translate(pipe.Pos.X - camera.X, pipe.Pos.Y - camera.Y, pipe.Pos.Z - camera.Z)
            .Translate(.5f, 0, .5f)
            .RotateY(rotorAngle)
            .Translate(-.5f, 0, -.5f);
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pipe.Pos.X, pipe.Pos.Y, pipe.Pos.Z, new Vec4f(1, 1, 1, 1));
        shader.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;
        capi.Render.RenderMesh(rotorMesh);
        shader.Stop();
    }

    private void SpawnNozzleParticles(float deltaTime, bool gas)
    {
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            bool nozzle = pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle;
            bool openPipe = pipe.IsPortOpenToAir(face);
            if (!nozzle && !openPipe) continue;
            double flow = pipe.GetNozzleFlowRate(face);
            if (Math.Abs(flow) < 0.01) continue;
            bool outward = flow > 0;
            double particlesPerSecond = outward
                ? 12 + Math.Min(100, Math.Abs(flow) * 8)
                : 5 + Math.Min(32, Math.Abs(flow) * 4);
            nozzleParticleAccumulators[face.Index] += deltaTime * particlesPerSecond;
            int quantity = (int)nozzleParticleAccumulators[face.Index];
            if (quantity <= 0) continue;
            nozzleParticleAccumulators[face.Index] -= quantity;

            Vec3f normal = new(face.Normali.X, face.Normali.Y, face.Normali.Z);
            if (!outward)
            {
                if (nozzle) SpawnNozzleIntakeParticles(quantity, normal, gas, Math.Abs(flow));
                continue;
            }

            float power = nozzle
                ? 1 + (MaximumNozzleOutputSpeedMultiplier - 1) * (float)Math.Min(
                    1, Math.Abs(flow) /
                        (HydraulicMath.NozzleInventoryRateLitresPerSecond * 4))
                : 1;
            float speed = (gas ? 0.55f : 0.35f) * power;
            Vec3f velocity = new(
                normal.X * speed,
                normal.Y * speed,
                normal.Z * speed);
            float spread = 0.16f;
            float particleScale = gas ? 0.24f : 0.18f;
            float originOffset = nozzle ? NozzleMouthOffset : PipeMouthOffset;
            float particleLifetime = gas ? 0.8f : 0.55f;
            Vec3d origin = pipe.Pos.ToVec3d().AddCopy(
                0.5 + normal.X * originOffset,
                0.5 + normal.Y * originOffset,
                0.5 + normal.Z * originOffset);
            capi.World.SpawnParticles(
                quantity,
                contentColor,
                origin.AddCopy(-0.025, -0.025, -0.025),
                origin.AddCopy(0.025, 0.025, 0.025),
                new Vec3f(velocity.X - spread, velocity.Y - spread, velocity.Z - spread),
                new Vec3f(velocity.X + spread, velocity.Y + spread, velocity.Z + spread),
                particleLifetime,
                gas ? 0.02f : 0.35f,
                particleScale,
                EnumParticleModel.Cube,
                null);
        }
    }

    private void SpawnNozzleIntakeParticles(
        int quantity,
        Vec3f normal,
        bool gas,
        double flowRate)
    {
        IntakeBasis(normal, out Vec3f tangent, out Vec3f bitangent);
        Random random = capi.World.Rand;
        float speed = 0.65f + (float)Math.Min(0.75, flowRate * 0.08);
        float scale = gas ? 0.14f : 0.10f;
        Vec3d target = pipe.Pos.ToVec3d().AddCopy(
            0.5 + normal.X * IntakeTargetOffset,
            0.5 + normal.Y * IntakeTargetOffset,
            0.5 + normal.Z * IntakeTargetOffset);
        for (int particle = 0; particle < quantity; particle++)
        {
            float axial = IntakeConeLength * (0.45f + 0.55f * (float)random.NextDouble());
            float radius = axial * 0.55f * MathF.Sqrt((float)random.NextDouble());
            float angle = (float)(random.NextDouble() * GameMath.TWOPI);
            float tangentOffset = MathF.Cos(angle) * radius;
            float bitangentOffset = MathF.Sin(angle) * radius;
            Vec3d start = pipe.Pos.ToVec3d().AddCopy(
                0.5 + normal.X * (NozzleMouthOffset + axial) +
                    tangent.X * tangentOffset + bitangent.X * bitangentOffset,
                0.5 + normal.Y * (NozzleMouthOffset + axial) +
                    tangent.Y * tangentOffset + bitangent.Y * bitangentOffset,
                0.5 + normal.Z * (NozzleMouthOffset + axial) +
                    tangent.Z * tangentOffset + bitangent.Z * bitangentOffset);
            double dx = target.X - start.X;
            double dy = target.Y - start.Y;
            double dz = target.Z - start.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            float lifetime = (float)(distance / speed);
            Vec3f velocity = new(
                (float)(dx / lifetime),
                (float)(dy / lifetime),
                (float)(dz / lifetime));
            capi.World.SpawnParticles(
                1, contentColor, start, start, velocity, velocity,
                lifetime, 0f, scale, EnumParticleModel.Cube, null);
        }
    }

    private static void IntakeBasis(Vec3f normal, out Vec3f tangent, out Vec3f bitangent)
    {
        if (Math.Abs(normal.Y) > 0.5f)
        {
            tangent = new Vec3f(1, 0, 0);
            bitangent = new Vec3f(0, 0, 1);
            return;
        }
        tangent = new Vec3f(0, 1, 0);
        bitangent = new Vec3f(normal.Z, 0, -normal.X);
    }

    private static int BrightenedParticleColor(int alpha, int red, int green, int blue) =>
        ColorUtil.ToRgba(alpha, Lighten(red), Lighten(green), Lighten(blue));

    private static int Lighten(int channel) => (int)Math.Round(
        channel + (255 - channel) * ParticleLightenFraction);

    private void SpawnSprinklerSpray(float deltaTime, float performance)
    {
        sprinklerParticleAccumulator += deltaTime * (60 + 240 * performance);
        int quantity = (int)sprinklerParticleAccumulator;
        if (quantity <= 0) return;
        sprinklerParticleAccumulator -= quantity;
        float maximumOutward = 0.8f + 1.7f * performance;
        float liftMin = 0.45f + 0.65f * performance;
        float liftMax = 0.9f + 1.5f * performance;
        float life = 0.75f + 0.85f * performance;
        float scale = 0.13f + 0.09f * performance;
        double[] baseAngles = { -GameMath.PIHALF, GameMath.PIHALF, GameMath.PI, 0 };
        Random random = capi.World.Rand;
        for (int particle = 0; particle < quantity; particle++)
        {
            int nozzle = nextSprinklerNozzle;
            nextSprinklerNozzle = (nextSprinklerNozzle + 1) % 4;
            // Matrixf.RotateY advances the model toward decreasing polar
            // angles in X/Z, so particle positions must subtract the same
            // rotor angle to stay attached to the visible nozzle.
            double nozzleAngle = baseAngles[nozzle] - rotorAngle;
            double directionX = Math.Cos(nozzleAngle);
            double directionZ = Math.Sin(nozzleAngle);
            Vec3d position = pipe.Pos.ToVec3d().AddCopy(
                .5 + directionX * .32, .045, .5 + directionZ * .32);

            double sprayAngle = nozzleAngle + (random.NextDouble() - .5) * .12;
            float horizontalSpeed = (float)random.NextDouble() * maximumOutward;
            float lift = liftMin + (liftMax - liftMin) * (float)random.NextDouble();
            Vec3f velocity = new(
                (float)Math.Cos(sprayAngle) * horizontalSpeed,
                lift,
                (float)Math.Sin(sprayAngle) * horizontalSpeed);
            capi.World.SpawnParticles(
                1, sprinklerColor,
                position.AddCopy(-.025, -.012, -.025), position.AddCopy(.025, .012, .025),
                velocity, velocity, life, 1f, scale, EnumParticleModel.Cube, null);
        }
    }

    private bool HasVisibleInterior()
    {
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.GetAddon(face) == HydraulicFaceAddon.GlassWindow ||
                pipe.IsPortOpenToAir(face)) return true;
        }
        return false;
    }

    public void Dispose()
    {
        rotorMesh.Dispose();
        contentMesh?.Dispose();
        contentMesh = null;
    }
}
