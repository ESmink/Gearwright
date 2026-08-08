using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Shows the pump's pressure-driven gauge, plunger, and direct-through liquid flow.</summary>
internal sealed class PassiveFluidPumpRenderer : IRenderer, IDisposable
{
    private const float FullPressureFrame = 29;

    private readonly BlockEntityPassiveFluidPump pump;
    private readonly ICoreClientAPI capi;
    private readonly AnimationUtil animationUtil;
    private readonly Matrixf modelMatrix = new();
    private ScrollingLiquidSurface? fluidSurface;
    private string? fluidMeshCode;
    private float displayedPressure;
    private float motionPhase;
    private float fluidPhase;

    public PassiveFluidPumpRenderer(BlockEntityPassiveFluidPump pump, ICoreClientAPI capi)
    {
        this.pump = pump;
        this.capi = capi;

        Shape shape = Shape.TryGet(capi, "gearwright:shapes/block/passive-fluid-pump-mechanism.json");
        animationUtil = new AnimationUtil(capi, pump.Pos.ToVec3d());
        animationUtil.InitializeShapeAndAnimator(
            "gearwright-passive-fluid-pump-mechanism",
            shape,
            capi.Tesselator.GetTextureSource(pump.Block),
            new Vec3f(0, -pump.Facing.HorizontalAngleIndex * 90, 0),
            out _);
        animationUtil.StartAnimation(new AnimationMetaData
        {
            Animation = "pressure",
            Code = "pressure",
            AnimationSpeed = 0,
            EaseInSpeed = 10000,
            EaseOutSpeed = 10000,
            BlendMode = EnumAnimationBlendMode.Average
        }.Init());
    }

    // Update the pose immediately before AnimationUtil's opaque renderer draws it.
    public double RenderOrder => 0.49;
    public int RenderRange => 48;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        PumpOffer localOffer = pump.GetOffer();
        bool hasInputPressure = localOffer.Pressure > 0;
        bool operating = hasInputPressure &&
            pump.NetworkStatusCode == "running" &&
            pump.CurrentLiquidCode?.Equals(localOffer.LiquidCode) == true;
        float performance = hasInputPressure ? (float)HydraulicMath.Performance(localOffer.Pressure) : 0;

        float response = 1 - MathF.Exp(-Math.Min(deltaTime, 0.25f) * 5.5f);
        displayedPressure += (performance - displayedPressure) * response;
        if (operating)
        {
            motionPhase = (motionPhase + deltaTime * (2.2f + 3.4f * performance)) % GameMath.TWOPI;
        }

        float livePulse = operating
            ? (MathF.Sin(motionPhase) * 0.009f + MathF.Sin(motionPhase * 1.83f + 0.7f) * 0.004f) * performance
            : 0;
        RunningAnimation? pressureAnimation = animationUtil.animator?.GetAnimationState("pressure");
        if (pressureAnimation != null)
        {
            pressureAnimation.CurrentFrame = Math.Clamp(displayedPressure + livePulse, 0, 1) * FullPressureFrame;
        }

        if (!hasInputPressure) return;
        EnsureFluidSurface(localOffer.LiquidCode);
        if (fluidSurface == null) return;

        if (operating)
        {
            float scrollSpeed = 0.05f + 0.62f * MathF.Pow(performance, 1.35f);
            fluidPhase = (fluidPhase + deltaTime * scrollSpeed) % 1;
        }

        fluidSurface.Update(fluidPhase, horizontal: true, reverse: false);
        RenderLiquidPane(fluidSurface.MeshRef, GameMath.PIHALF, 0.62f, fluidSurface.TextureId);
        fluidSurface.Update(fluidPhase, horizontal: true, reverse: true);
        RenderLiquidPane(fluidSurface.MeshRef, -GameMath.PIHALF, 0.62f, fluidSurface.TextureId);
    }

    private void RenderLiquidPane(MeshRef mesh, float localYaw, float opacity, int textureId)
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity()
            .Translate(pump.Pos.X - camera.X, pump.Pos.Y - camera.Y, pump.Pos.Z - camera.Z)
            .Translate(0.5f, 0.5f, 0.5f)
            .RotateY(FacingYaw(pump.Facing))
            .RotateY(localYaw)
            .Translate(-0.5f, -0.5f, -0.5f);

        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pump.Pos.X, pump.Pos.Y, pump.Pos.Z, new Vec4f(1, 1, 1, opacity));
        shader.Tex2D = textureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = 0.05f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
        capi.Render.GlToggleBlend(false);
    }

    private void EnsureFluidSurface(AssetLocation liquidCode)
    {
        string code = liquidCode.ToString();
        if (fluidMeshCode == code && fluidSurface != null) return;
        fluidSurface?.Dispose();
        fluidSurface = null;
        fluidMeshCode = code;

        Item? item = capi.World.GetItem(liquidCode);
        WaterTightContainableProps? props = item?.Attributes?["waterTightContainerProps"]
            .AsObject<WaterTightContainableProps>(null, liquidCode.Domain);
        if (item == null || props?.Texture == null) return;

        try
        {
            ContainerTextureSource textureSource = new(capi, new ItemStack(item), props.Texture);
            TextureAtlasPosition position = textureSource["liquid"];
            fluidSurface = new ScrollingLiquidSurface(
                capi, position,
                4.7f / 16f, 6.72f / 16f,
                11.3f / 16f, 9.28f / 16f,
                3.58f / 16f);
        }
        catch (Exception exception)
        {
            capi.Logger.Error(
                "[Gearwright] Could not prepare the passive fluid pump liquid texture for {0}: {1}",
                liquidCode, exception.Message);
        }
    }

    private static float FacingYaw(BlockFacing face)
    {
        if (face == BlockFacing.EAST) return -GameMath.PIHALF;
        if (face == BlockFacing.SOUTH) return GameMath.PI;
        if (face == BlockFacing.WEST) return GameMath.PIHALF;
        return 0;
    }

    public void Dispose()
    {
        animationUtil.Dispose();
        fluidSurface?.Dispose();
        fluidSurface = null;
    }
}
