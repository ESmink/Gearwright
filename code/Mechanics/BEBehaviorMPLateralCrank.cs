using System;
using System.Text;
using Gearwright.Hydraulics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

public sealed class BEBehaviorMPLateralCrank : BEBehaviorMPBase
{
    private const string StorageKey = "gearwrightLateralCrank";
    private const double AngleStepDegrees = 45;
    private ITreeAttribute preservedState = new TreeAttribute();
    private bool canWrite = true;
    private double phaseOffsetDegrees;
    private LateralCrankRenderer? renderer;

    public BEBehaviorMPLateralCrank(BlockEntity blockentity) : base(blockentity) { }

    public double PhaseOffsetRadians => phaseOffsetDegrees * GameMath.DEG2RAD;
    public bool HasAttachedPump => AttachedPump != null;
    internal float SampledFluidResistance { get; private set; }

    private BlockFacing RotationAxis => Blockentity.Block.Variant["rotation"] == "we"
        ? BlockFacing.WEST : BlockFacing.NORTH;

    internal double AngleInFrame(BlockFacing localX, BlockFacing localY) =>
        LateralCrankMotion.AngleInFrame(
            AngleRad + PhaseOffsetRadians, RotationAxis, localX, localY);

    internal double PumpAngle(BlockEntityReciprocatingPump pump) =>
        AngleInFrame(pump.OutputFace, pump.DriveFace);

    internal double PumpTravel(BlockEntityReciprocatingPump pump, double networkTravel) =>
        networkTravel * GearedRatio * (IsRotationReversed() ? -1 : 1) *
        (pump.OutputFace == RotationAxis ? 1 : -1);

    internal BlockEntityReciprocatingPump? AttachedPump
    {
        get
        {
            EnumAxis shaftAxis = AxisFaces()[0].Axis;
            foreach (BlockFacing face in AttachmentFaces())
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Position.AddCopy(face)) is
                    BlockEntityReciprocatingPump pump &&
                    pump.DriveFace == face.Opposite &&
                    pump.OutputFace.Axis == shaftAxis)
                {
                    return pump;
                }
            }
            return null;
        }
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api is ICoreClientAPI capi)
        {
            renderer = new LateralCrankRenderer(this, capi);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-lateral-crank-pose");
        }
    }

    // The independently phased journal is rendered by LateralCrankRenderer.
    // Returning null prevents the vanilla mechanical renderer from drawing a
    // second, unphased copy of the model over it.
    protected override CompositeShape GetShape() => null!;

    public override void SetOrientations()
    {
        Vec3i axis = RotationAxis.Normali;
        AxisSign = new[] { axis.X, axis.Y, axis.Z };
    }

    public override float GetResistance()
    {
        BlockEntityReciprocatingPump? pump = AttachedPump;
        return pump == null
            ? ReciprocatingPumpMath.BaseMechanicalResistance
            : pump.GetMechanicalResistance(PumpAngle(pump), PumpTravel(pump, (Network?.Speed ?? 0) * .5));
    }

    public override float GetTorque(long tick, float speed, out float resistance)
    {
        resistance = GetResistance();
        SampledFluidResistance = Math.Max(0, resistance - ReciprocatingPumpMath.BaseMechanicalResistance);
        return 0;
    }

    public void CycleJournalAngle(IPlayer byPlayer)
    {
        if (!canWrite || HasAttachedPump || Api?.Side != EnumAppSide.Server) return;
        phaseOffsetDegrees = (phaseOffsetDegrees + AngleStepDegrees) % 360;
        Blockentity.MarkDirty(true);
        Api.World.PlaySoundAt(
            new AssetLocation("game:sounds/block/ratchet"), Position, 0, byPlayer,
            randomizePitch: false, range: 12, volume: .7f);
    }

    public void RefreshVisualTopology() => renderer?.InvalidateTopology();

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        ITreeAttribute? state = tree.GetTreeAttribute(StorageKey);
        if (state == null)
        {
            preservedState = new TreeAttribute();
            canWrite = true;
            phaseOffsetDegrees = 0;
            return;
        }
        int? schema = state.TryGetInt("schemaVersion");
        preservedState = LateralCrankStateSchema.PrepareForRead(state, out canWrite, out string? problem);
        phaseOffsetDegrees = NormalizeDegrees(preservedState.GetDouble("phaseOffsetDegrees", 0));
        if (!canWrite)
        {
            worldAccessForResolve.Logger.Error(
                "[Gearwright] Reciprocating drive shaft state at {0} has a {1} schema ({2}). The original data will remain read-only.",
                Position, problem, schema?.ToString() ?? "non-integer");
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (!canWrite)
        {
            tree[StorageKey] = preservedState.Clone();
            return;
        }
        ITreeAttribute state = preservedState.Clone();
        state.SetInt("schemaVersion", LateralCrankStateSchema.CurrentVersion);
        state.SetDouble("phaseOffsetDegrees", phaseOffsetDegrees);
        preservedState = state.Clone();
        tree[StorageKey] = state;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);
        sb.AppendLine(Lang.Get("gearwright:lateral-crank-angle", phaseOffsetDegrees));
        if (HasAttachedPump) sb.AppendLine(Lang.Get("gearwright:lateral-crank-angle-locked"));
        if (!canWrite) sb.AppendLine(Lang.Get("gearwright:lateral-crank-state-read-only"));
    }

    public override void OnBlockRemoved()
    {
        ShutdownRenderer();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        ShutdownRenderer();
        base.OnBlockUnloaded();
    }

    internal BlockFacing[] AxisFaces() => BlockLateralCrank.FacesFor(
        Blockentity.Block.Variant["rotation"]);

    private BlockFacing[] AttachmentFaces() => Blockentity.Block.Variant["rotation"] == "we"
        ? new[] { BlockFacing.UP, BlockFacing.DOWN, BlockFacing.NORTH, BlockFacing.SOUTH }
        : new[] { BlockFacing.UP, BlockFacing.DOWN, BlockFacing.WEST, BlockFacing.EAST };

    private void ShutdownRenderer()
    {
        if (renderer == null || Api is not ICoreClientAPI capi) return;
        capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
        renderer.Dispose();
        renderer = null;
    }

    private static double NormalizeDegrees(double value)
    {
        if (!double.IsFinite(value)) return 0;
        value %= 360;
        return value < 0 ? value + 360 : value;
    }
}
