using System;
using System.Collections.Generic;
using System.Text;
using Gearwright.Air;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Mechanics;

/// <summary>Automatic Bellow drive behavior; one selected crank owns its load.</summary>
public sealed class BEBehaviorLargeBellows : BlockEntityBehavior, IReciprocatingDriveDevice
{
    internal const string StorageKey = "gearwrightLargeBellows";
    private ITreeAttribute preserved = new TreeAttribute();
    private bool writable = true;
    private double air;
    private double syncSeconds;
    private double lastSentAir = -1;
    private LargeBellowsRenderer? renderer;
    private BEBehaviorMPLateralCrank? displayedDrive;
    public BEBehaviorLargeBellows(BlockEntity blockentity) : base(blockentity) { }
    public BlockPos Position => Pos;
    public BlockFacing NozzleFace => BlockFacing.FromCode(Block.Variant["side"]) ?? BlockFacing.NORTH;
    public BlockFacing ShaftFace => NozzleFace.Opposite.GetCW();
    public BlockPos RearPosition => Pos.AddCopy(NozzleFace.Opposite);
    public BlockFacing DriveFace => SelectedDrive()?.Position.Y > Pos.Y ? BlockFacing.UP : BlockFacing.DOWN;
    internal double ReservoirFill => air / LargeBellowsStateSchema.Capacity;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        Blockentity.RegisterGameTickListener(Tick, 100);
        if (api is ICoreClientAPI client)
        {
            renderer = new(this, client);
            client.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-large-bellows");
        }
    }

    internal BEBehaviorMPLateralCrank? SelectedDrive()
    {
        if (!writable || Api == null) return null;
        var accessor = Api.World.BlockAccessor;
        // A stopped, aligned lower crank retains ownership. Never inspect an unloaded chunk.
        foreach (BlockFacing face in new[] { BlockFacing.DOWN, BlockFacing.UP })
        {
            BlockPos location = RearPosition.AddCopy(face);
            if (accessor.GetChunkAtBlockPos(location) == null) return null;
            var drive = accessor.GetBlockEntity(location)?.GetBehavior<BEBehaviorMPLateralCrank>();
            if (drive != null && drive.AxisFaces()[0].Axis == ShaftFace.Axis) return drive;
        }
        return null;
    }

    internal static IEnumerable<BEBehaviorLargeBellows> NearCrank(IWorldAccessor world, BlockPos pos)
    {
        // The vanilla large bellows is anchored at its nozzle half; its drive
        // is beside the rear half, one horizontal block away from that entity.
        foreach (BlockFacing vertical in new[] { BlockFacing.DOWN, BlockFacing.UP })
        foreach (BlockFacing nozzle in BlockFacing.HORIZONTALS)
        {
            BlockPos owner = pos.AddCopy(vertical).AddCopy(nozzle);
            if (world.BlockAccessor.GetChunkAtBlockPos(owner) == null) continue;
            var bellows = world.BlockAccessor.GetBlockEntity(owner)?.GetBehavior<BEBehaviorLargeBellows>();
            if (bellows != null && bellows.NozzleFace == nozzle) yield return bellows;
        }
    }

    public float SampleReciprocatingLoad(double angle, double travel, double seconds)
    {
        bool top = DriveFace == BlockFacing.UP;
        double phase = LargeBellowsMotion.Phase(angle, top);
        double derivative = (LargeBellowsMotion.Volume(phase + .0001, top) -
            LargeBellowsMotion.Volume(phase - .0001, top)) / .0002;
        double rate = -derivative * Math.Sign(-travel);
        return (float)(.001 + .08 * Math.Max(0, rate));
    }

    public void StepReciprocatingDrive(double seconds)
    {
        if (Api?.Side != EnumAppSide.Server || !writable || !double.IsFinite(seconds) || seconds <= 0) return;
        var drive = SelectedDrive();
        if (drive?.Network == null) return;
        bool top = DriveFace == BlockFacing.UP;
        double phase = LargeBellowsMotion.Phase(drive.DeviceAngle(this), top);
        double travel = -drive.DeviceTravel(this, drive.Network.Speed * seconds * 5);
        double incoming = LargeBellowsMotion.PumpedAir(phase - travel, phase, top);
        if (top) Deliver(incoming, seconds);
        else air = Math.Min(LargeBellowsStateSchema.Capacity, air + incoming);
    }

    private void Tick(float seconds)
    {
        var drive = SelectedDrive();
        if (Api is ICoreClientAPI && !ReferenceEquals(drive, displayedDrive))
        {
            displayedDrive = drive;
            Blockentity.MarkDirty(true);
        }
        if (Api.Side != EnumAppSide.Server || !writable) return;
        if (air > 0)
        {
            double elapsed = Math.Clamp(seconds, 0, .2f);
            double released = Math.Min(air, elapsed * .15);
            air -= released;
            Deliver(released, elapsed);
        }
        syncSeconds = Math.Min(.25, syncSeconds + seconds);
        if (syncSeconds >= .25 && Math.Abs(lastSentAir - air) > .00001)
        {
            syncSeconds = 0;
            lastSentAir = air;
            Blockentity.MarkDirty();
        }
    }

    private void Deliver(double amount, double seconds)
    {
        if (amount <= 0 || seconds <= 0) return;
        AirDelivery.Deliver(Api, Pos, new AirFlow(amount / seconds, seconds, NozzleFace));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        if (tree[StorageKey] == null) { preserved = new TreeAttribute(); malformed = null; air = 0; writable = true; return; }
        if (tree.GetTreeAttribute(StorageKey) is not ITreeAttribute state)
        {
            malformed = tree[StorageKey].Clone(); writable = false;
            world.Logger.Error("[Gearwright] Large bellows state at {0} is malformed; original data is read-only.", Pos);
            return;
        }
        malformed = null;
        preserved = LargeBellowsStateSchema.Read(state, out writable, out string? problem);
        air = writable ? preserved.GetDouble("air", 0) : 0;
        if (!writable) world.Logger.Error("[Gearwright] Large bellows state at {0} is {1}; original data is read-only.", Pos, problem);
    }
    private IAttribute? malformed;

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (malformed != null) { tree[StorageKey] = malformed.Clone(); return; }
        ITreeAttribute state = preserved.Clone();
        if (writable) { state.SetInt("schemaVersion", 1); state.SetDouble("air", air); }
        tree[StorageKey] = state;
        preserved = state.Clone();
    }

    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        var drive = SelectedDrive();
        text.AppendLine(Lang.Get(!writable ? "gearwright:bellows-readonly" : drive == null
            ? "gearwright:bellows-unconnected" : DriveFace == BlockFacing.DOWN
                ? "gearwright:bellows-bottom" : "gearwright:bellows-top"));
    }

    public override void OnBlockRemoved() { Shutdown(); base.OnBlockRemoved(); }
    public override void OnBlockUnloaded() { Shutdown(); base.OnBlockUnloaded(); }
    private void Shutdown()
    {
        if (renderer == null || Api is not ICoreClientAPI client) return;
        client.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
        renderer.Dispose(); renderer = null;
    }
}
