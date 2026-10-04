using Gearwright.Air;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using System.Text;
using System;
using Gearwright.Audio;

namespace Gearwright.Pneumatics;

/// <summary>
/// Finite server-side bellows reservoir. The approved grounded accumulator adds
/// three physical inlets without changing the legacy intake's persisted fields.
/// </summary>
public sealed class BlockEntityPneumaticAirIntake : BlockEntity, IAirReceiver
{
    internal const string StorageKey = "gearwrightPneumaticIntake";
    private PneumaticIntakeState state = PneumaticIntakeState.Read(null);
    private PneumaticRenderer? renderer;
    private LocalMachineLoop? airflowSound;
    private long soundTick;

    public bool CanWriteState => state.CanWrite;
    public BlockFacing Outlet => state.Outlet;
    public double StoredAir => state.StoredAir;
    private bool GroundedAccumulator => Block?.Code?.Path == "pneumatic-accumulator";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is ICoreClientAPI client)
        {
            renderer = new PneumaticRenderer(this, client);
            client.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-pneumatic-accumulator");
            client.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "gearwright-pneumatic-accumulator-glass");
            airflowSound = new(client, Pos, "airflow");
            soundTick = RegisterGameTickListener(seconds => airflowSound?.Update(seconds,
                state.CanWrite && (!GroundedAccumulator || Outlet.IsHorizontal)
                    ? Math.Sqrt(PneumaticAir.OutletRate(StoredAir) / PneumaticAir.OutletUnitsPerSecond) : 0), 100);
        }
        else api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Register(this);
    }

    public void ReceiveAir(IWorldAccessor world, BlockPos position, AirFlow flow)
    {
        if (!CanSimulate || !ReferenceEquals(world, Api.World) || !Pos.Equals(position) || !flow.IsValid || !PneumaticAir.ValidFace(flow.Direction)) return;
        // The approved grounded accumulator exposes only horizontal sockets.
        // Keep schema-1's saved outlet and air quantity intact, including legacy
        // vertical orientation; an incompatible physical orientation pauses it.
        if (GroundedAccumulator && (!Outlet.IsHorizontal || !flow.Direction.IsHorizontal)) return;
        if (state.Receive(flow) > 0) MarkDirty(false);
    }

    internal double ReleaseAir(double seconds)
    {
        if (!CanSimulate || GroundedAccumulator && !Outlet.IsHorizontal) return 0;
        double released = state.Release(seconds);
        if (released > 0) MarkDirty(false);
        return released;
    }

    // Placement/menu callers must validate claims before invoking this method.
    // This adapter has no client packet or interaction handler.
    internal bool SetOutlet(BlockFacing outlet)
    {
        if (!CanSimulate || GroundedAccumulator && !outlet.IsHorizontal || !state.SetOutlet(outlet)) return false;
        MarkDirty(true);
        return true;
    }

    private bool CanSimulate => state.CanWrite && Api?.Side == EnumAppSide.Server &&
        Api.World.BlockAccessor.GetChunkAtBlockPos(Pos) != null;

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        state = PneumaticIntakeState.Read(tree[StorageKey]);
        if (!state.CanWrite)
        {
            worldAccessForResolve.Logger.Error(
                "[Gearwright] Pneumatic intake state at {0} is {1}. Air intake and release are paused; the original data remains read-only.",
                Pos, state.Problem);
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree[StorageKey] = state.Write();
    }

    public override bool OnTesselation(ITerrainMeshPool pool, ITesselatorAPI tesselator) => true;
    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        base.GetBlockInfo(player, text);
        text.AppendLine(Lang.Get("gearwright:pneumatic-reserve", (int)(100 * StoredAir / PneumaticAir.PlenumCapacity)));
        text.AppendLine(Lang.Get("gearwright:pneumatic-outlet", Outlet.Code));
    }
    public override void OnBlockRemoved() { Unregister(); base.OnBlockRemoved(); }
    public override void OnBlockUnloaded() { Unregister(); base.OnBlockUnloaded(); }
    private void Unregister()
    {
        if (soundTick != 0) { UnregisterGameTickListener(soundTick); soundTick = 0; }
        airflowSound?.Dispose(); airflowSound = null;
        if (Api is ICoreClientAPI client && renderer != null)
        {
            client.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            client.Event.UnregisterRenderer(renderer, EnumRenderStage.OIT);
            renderer.Dispose(); renderer = null;
        }
        else if (Api != null) Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Unregister(this);
    }
}
