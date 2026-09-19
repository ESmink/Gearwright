using System;
using System.Collections.Generic;
using System.Linq;
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
    public bool HasAttachedPump => AttachedDevices.Any(device => device is BlockEntityReciprocatingPump);
    public bool HasAttachedDevice => AttachedDevices.Length > 0;
    internal float SampledDeviceResistance { get; private set; }

    private BlockFacing RotationAxis => Blockentity.Block.Variant["rotation"] == "we"
        ? BlockFacing.WEST : BlockFacing.NORTH;

    internal double AngleInFrame(BlockFacing localX, BlockFacing localY) =>
        LateralCrankMotion.AngleInFrame(
            AngleRad + PhaseOffsetRadians, RotationAxis, localX, localY);

    internal double PumpAngle(BlockEntityReciprocatingPump pump) =>
        DeviceAngle(pump);

    internal double DeviceAngle(IReciprocatingDriveDevice device) =>
        AngleInFrame(device.ShaftFace, device.DriveFace);

    internal double PumpTravel(BlockEntityReciprocatingPump pump, double networkTravel) =>
        DeviceTravel(pump, networkTravel);

    internal double DeviceTravel(IReciprocatingDriveDevice device, double networkTravel) =>
        networkTravel * GearedRatio * (IsRotationReversed() ? -1 : 1) *
        (device.ShaftFace == RotationAxis ? 1 : -1);

    internal IReciprocatingDriveDevice[] AttachedDevices
    {
        get
        {
            EnumAxis shaftAxis = AxisFaces()[0].Axis;
            List<IReciprocatingDriveDevice> devices = new(4);
            foreach (BlockFacing face in ReciprocatingDriveMount.Faces(shaftAxis))
            {
                BlockPos adjacent = Position.AddCopy(face);
                if (Api?.World.BlockAccessor.GetChunkAtBlockPos(adjacent) != null &&
                    Api.World.BlockAccessor.GetBlockEntity(adjacent) is IReciprocatingDriveDevice device &&
                    device.DriveFace == face.Opposite && device.ShaftFace.Axis == shaftAxis)
                {
                    devices.Add(device);
                }
            }
            return devices.ToArray();
        }
    }

    internal float DeviceOffset(IReciprocatingDriveDevice device)
    {
        IReciprocatingDriveDevice[] devices = AttachedDevices;
        int index = Array.IndexOf(devices, device);
        if (index < 0) return 0;
        float offset = ReciprocatingDriveMount.CenteredOffset(index, devices.Length);
        return device.ShaftFace == RotationAxis ? -offset : offset;
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
        double load = ReciprocatingDriveMount.BearingResistance;
        foreach (IReciprocatingDriveDevice device in AttachedDevices)
            load += Math.Max(0, device.SampleReciprocatingLoad(DeviceAngle(device),
                DeviceTravel(device, (Network?.Speed ?? 0) * .5), .1));
        return (float)Math.Min(float.MaxValue, load);
    }

    public override float GetTorque(long tick, float speed, out float resistance)
    {
        resistance = GetResistance();
        SampledDeviceResistance = Math.Max(0, resistance - ReciprocatingDriveMount.BearingResistance);
        return 0;
    }

    public void CycleJournalAngle(IPlayer byPlayer)
    {
        if (!canWrite || HasAttachedDevice || Api?.Side != EnumAppSide.Server) return;
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
        if (HasAttachedDevice) sb.AppendLine(Lang.Get("gearwright:lateral-crank-angle-locked"));
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
