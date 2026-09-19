using System;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Poses the approved mechanism from crank angle and shows chamber content behind the glass.</summary>
internal sealed class ReciprocatingPumpRenderer : IRenderer, IDisposable
{
    private readonly BlockEntityReciprocatingPump pump;
    private readonly ICoreClientAPI capi;
    private readonly MeshRef pistonMesh;
    private readonly MeshRef connectingRodMesh;
    private readonly MeshRef wetIntakeMesh;
    private readonly MeshRef wetOutputMesh;
    private readonly MeshRef breatherIntakeMesh;
    private readonly MeshRef breatherExhaustMesh;
    private readonly Matrixf modelMatrix = new();
    private float[] orientation = Mat4f.Create();
    private ScrollingLiquidSurface? contentSurface;
    private ScrollingLiquidSurface? contentTopSurface;
    private string? contentMeshCode;
    private float texturePhase;
    private float displayedScrollSpeed;
    private double? previousVolume;
    private ReciprocatingPumpStroke displayedStroke;

    public ReciprocatingPumpRenderer(BlockEntityReciprocatingPump pump, ICoreClientAPI capi)
    {
        this.pump = pump;
        this.capi = capi;
        pistonMesh = Upload("piston");
        connectingRodMesh = Upload("connecting-rod");
        wetIntakeMesh = Upload("wet-intake-check");
        wetOutputMesh = Upload("wet-output-check");
        breatherIntakeMesh = Upload("breather-intake-check");
        breatherExhaustMesh = Upload("breather-exhaust-check");
        RefreshOrientation();
    }

    public double RenderOrder => .5;
    public int RenderRange => 48;

    public void RefreshOrientation()
    {
        orientation = PumpOrientation.Matrix(pump.OutputFace, pump.DriveFace);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        bool synchronized = PumpTimingSystem.TryPresentation(pump, out PumpPresentation presentation);
        double angle = synchronized ? presentation.Angle : LiveAngle();
        double volume = ReciprocatingPumpMath.ChamberVolumeLitres(angle);
        // Use one presentation frame for motion, contents and wet checks. Before
        // its first packet, derive the fallback stroke from the live journal.
        displayedStroke = synchronized ? (ReciprocatingPumpStroke)presentation.Stroke : previousVolume.HasValue
            ? ReciprocatingPumpMath.Stroke(previousVolume.Value, volume)
            : ReciprocatingPumpStroke.Stationary;
        previousVolume = volume;
        ReciprocatingPumpVisualPose pose = ReciprocatingPumpMath.VisualPose(angle, displayedStroke);
        if (synchronized)
        {
            pose = pose with
            {
                WetIntakeOffsetY = presentation.IntakeOpen ? .36f / 16 : 0,
                WetOutputOffsetY = presentation.OutputOpen ? -.36f / 16 : 0
            };
        }
        float rodOffset = synchronized ? presentation.RodOffsetX : pump.FindCrank()?.DeviceOffset(pump) ?? 0;
        RenderMechanism(pose, rodOffset, synchronized || !PumpTimingSystem.HasPresentationNetwork(pump));

        AssetLocation? content = synchronized
            ? string.IsNullOrEmpty(presentation.Content) ? null : new AssetLocation(presentation.Content)
            : pump.CurrentContentCode;
        double amount = synchronized ? presentation.Amount : pump.ContentAmountLitres;
        double throughput = synchronized ? presentation.Throughput : pump.ThroughputLitresPerSecond;
        if (content == null || amount <= 0) return;
        EnsureContentSurface(content);
        if (contentSurface == null) return;

        PipeContentPhase phase = PipeContent.Phase(capi.World, content);
        float target = throughput <= 0
            ? 0
            : (float)HydraulicMath.TextureScrollCyclesPerSecond(
                throughput, phase) * 8 * (displayedStroke == ReciprocatingPumpStroke.Suction ? -1 : 1);
        float response = 1 - MathF.Exp(-Math.Min(deltaTime, .25f) * 8);
        displayedScrollSpeed += (target - displayedScrollSpeed) * response;
        texturePhase = (texturePhase + deltaTime * displayedScrollSpeed) % 1;

        float pistonBottom = ReciprocatingPumpLiquidGeometry.UpperPistonBottom + pose.PistonOffsetY;
        bool gas = phase == PipeContentPhase.Gas;
        float visibleTop = gas
            ? pistonBottom
            : ReciprocatingPumpLiquidGeometry.SurfaceHeight(amount, pistonBottom, volume);
        if (visibleTop <= ReciprocatingPumpLiquidGeometry.Bottom + .001f) return;
        contentSurface.Update(
            texturePhase,
            horizontal: true,
            reverse: false,
            minYOverride: ReciprocatingPumpLiquidGeometry.Bottom,
            maxYOverride: visibleTop);
        float opacity = gas ? .08f + .30f * (float)(1 - Math.Exp(-amount / ReciprocatingPumpMath.StrokeCapacityLitres)) : .48f;
        capi.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        // The translucent side panes must not depth-occlude the top behind them.
        // Opaque walls and the piston still occlude all liquid surfaces normally.
        capi.Render.GLDepthMask(false);
        try
        {
            if (!gas && contentTopSurface != null)
            {
                contentTopSurface.Update(texturePhase, horizontal: true,
                    reverse: false);
                ReciprocatingPumpLiquidGeometry.ApplyTopPose(BeginModel(), visibleTop);
                RenderContentMesh(contentTopSurface.MeshRef, opacity, contentTopSurface.TextureId);
            }
            RenderPane(contentSurface.MeshRef, 0, opacity, contentSurface.TextureId);
            RenderPane(contentSurface.MeshRef, GameMath.PI, opacity, contentSurface.TextureId);
        }
        finally
        {
            capi.Render.GLDepthMask(true);
            capi.Render.GlToggleBlend(false);
        }
    }

    private double LiveAngle()
    {
        BEBehaviorMPLateralCrank? crank = pump.FindCrank();
        return crank == null ? 0 : crank.PumpAngle(pump);
    }

    private MeshRef Upload(string suffix)
    {
        Shape shape = Shape.TryGet(capi, $"gearwright:shapes/block/reciprocating-pump-{suffix}.json");
        capi.Tesselator.TesselateShape(pump.Block, shape, out MeshData mesh, new Vec3f(), null, null);
        return capi.Render.UploadMesh(mesh);
    }

    private Matrixf BeginModel()
    {
        Vec3d camera = capi.World.Player.Entity.CameraPos;
        modelMatrix.Identity().Translate(
            pump.Pos.X - camera.X, pump.Pos.Y - camera.Y, pump.Pos.Z - camera.Z);
        Mat4f.Multiply(modelMatrix.Values, modelMatrix.Values, orientation);
        return modelMatrix;
    }

    private void RenderMechanism(ReciprocatingPumpVisualPose pose, float rodOffset, bool showRod)
    {
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pump.Pos.X, pump.Pos.Y, pump.Pos.Z, new Vec4f(1, 1, 1, 1));
        shader.Tex2D = capi.BlockTextureAtlas.AtlasTextures[0].TextureId;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;

        RenderSlidingPart(shader, pistonMesh, pose.PistonOffsetY);
        // The approved rod is centered at (8,24,8), with its two bearings
        // three model units above/below that pivot. Translate AND rotate it.
        PumpOrientation.ApplyConnectingRodPose(BeginModel().Translate(rodOffset, 0, 0), pose);
        shader.ModelMatrix = modelMatrix.Values;
        if (showRod) capi.Render.RenderMesh(connectingRodMesh);
        RenderSlidingPart(shader, wetIntakeMesh, pose.WetIntakeOffsetY);
        RenderSlidingPart(shader, wetOutputMesh, pose.WetOutputOffsetY);
        RenderSlidingPart(shader, breatherIntakeMesh, pose.BreatherIntakeOffsetY);
        RenderSlidingPart(shader, breatherExhaustMesh, pose.BreatherExhaustOffsetY);
        shader.Stop();
    }

    private void RenderSlidingPart(IStandardShaderProgram shader, MeshRef mesh, float offsetY)
    {
        BeginModel().Translate(0, offsetY, 0);
        shader.ModelMatrix = modelMatrix.Values;
        capi.Render.RenderMesh(mesh);
    }

    private void EnsureContentSurface(AssetLocation contentCode)
    {
        string code = contentCode.ToString();
        if (contentSurface != null && contentMeshCode == code) return;
        contentSurface?.Dispose();
        contentTopSurface?.Dispose();
        contentSurface = null;
        contentTopSurface = null;
        contentMeshCode = code;

        TextureAtlasPosition? position = null;
        if (PipeContent.IsSteam(contentCode))
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
                    ContainerTextureSource source = new(capi, new ItemStack(item), props.Texture);
                    position = source["liquid"];
                }
                catch (Exception exception)
                {
                    capi.Logger.Error(
                        "[Gearwright] Could not prepare reciprocating pump content texture for {0}: {1}",
                        contentCode, exception.Message);
                }
            }
        }
        if (position == null) return;
        contentSurface = new ScrollingLiquidSurface(
            capi,
            position,
            ReciprocatingPumpLiquidGeometry.MinX,
            ReciprocatingPumpLiquidGeometry.Bottom,
            ReciprocatingPumpLiquidGeometry.MaxX,
            ReciprocatingPumpLiquidGeometry.UpperPistonBottom,
            ReciprocatingPumpLiquidGeometry.MinZ);
        if (PipeContent.Phase(capi.World, contentCode) != PipeContentPhase.Gas)
        {
            contentTopSurface = new ScrollingLiquidSurface(
                capi, position,
                ReciprocatingPumpLiquidGeometry.MinX,
                ReciprocatingPumpLiquidGeometry.MinZ,
                ReciprocatingPumpLiquidGeometry.MaxX,
                ReciprocatingPumpLiquidGeometry.MaxZ,
                0);
        }
    }

    private void RenderPane(MeshRef mesh, float localYaw, float opacity, int textureId)
    {
        BeginModel();
        if (localYaw != 0)
        {
            modelMatrix.Translate(.5f, .5f, .5f)
                .RotateY(localYaw)
                .Translate(-.5f, -.5f, -.5f);
        }

        RenderContentMesh(mesh, opacity, textureId);
    }

    private void RenderContentMesh(MeshRef mesh, float opacity, int textureId)
    {
        IStandardShaderProgram shader = capi.Render.PreparedStandardShader(
            pump.Pos.X, pump.Pos.Y, pump.Pos.Z, new Vec4f(1, 1, 1, opacity));
        shader.Tex2D = textureId;
        shader.ModelMatrix = modelMatrix.Values;
        shader.ViewMatrix = capi.Render.CameraMatrixOriginf;
        shader.ProjectionMatrix = capi.Render.CurrentProjectionMatrix;
        shader.AlphaTest = .02f;
        capi.Render.RenderMesh(mesh);
        shader.Stop();
    }

    public void Dispose()
    {
        pistonMesh.Dispose();
        connectingRodMesh.Dispose();
        wetIntakeMesh.Dispose();
        wetOutputMesh.Dispose();
        breatherIntakeMesh.Dispose();
        breatherExhaustMesh.Dispose();
        contentSurface?.Dispose();
        contentTopSurface?.Dispose();
        contentSurface = null;
        contentTopSurface = null;
    }
}
