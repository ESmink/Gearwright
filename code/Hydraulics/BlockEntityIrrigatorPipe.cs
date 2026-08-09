using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>A straight bronze pipe which irrigates through three drilled outlets.</summary>
public sealed class BlockEntityIrrigatorPipe : BlockEntityFluidPipe
{
    public const int NegativeSupport = IrrigatorSupportPlanner.NegativeSupport;
    public const int PositiveSupport = IrrigatorSupportPlanner.PositiveSupport;
    public const string DefaultSupportPlank = "game:planks-oak-ud";

    private bool alongX;
    private int supportMask = NegativeSupport | PositiveSupport;
    private string supportPlankCode = DefaultSupportPlank;
    private IrrigatorPipeRenderer? irrigatorRenderer;
    private bool supportRegistered;

    public bool AlongX => alongX;
    public int SupportMask => supportMask;
    public BlockFacing NegativeFace => alongX ? BlockFacing.WEST : BlockFacing.NORTH;
    public BlockFacing PositiveFace => alongX ? BlockFacing.EAST : BlockFacing.SOUTH;

    public void ConfigureAxis(bool placeAlongX)
    {
        if (!CanWriteState) return;
        alongX = placeAlongX;
        if (Api?.Side == EnumAppSide.Client)
        {
            MarkHydraulicsDirty(true);
            return;
        }
        ConfigureStraightPorts(NegativeFace, PositiveFace);
        MarkHydraulicsDirty(true);
    }

    internal void SetSupportMask(int nextMask)
    {
        nextMask &= NegativeSupport | PositiveSupport;
        if (supportMask == nextMask || !CanWriteState) return;
        supportMask = nextMask;
        MarkHydraulicsDirty(true);
        Api?.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            api.ModLoader.GetModSystem<IrrigatorSupportSystem>().Register(this);
            supportRegistered = true;
        }
        else if (api is ICoreClientAPI capi)
        {
            irrigatorRenderer = new IrrigatorPipeRenderer(this, capi);
            capi.Event.RegisterRenderer(
                irrigatorRenderer, EnumRenderStage.Opaque, "gearwright-irrigator-pipe");
        }
    }

    public override void OnBlockBroken(IPlayer byPlayer)
    {
        StopIrrigatorRenderer();
        UnregisterSupport();
        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        StopIrrigatorRenderer();
        UnregisterSupport();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopIrrigatorRenderer();
        UnregisterSupport();
        base.OnBlockUnloaded();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("gearwright:irrigator-pressure", CurrentPressure));
        dsc.AppendLine(Lang.Get(
            "gearwright:irrigator-performance",
            HydraulicMath.IrrigatorPerformance(CurrentPressure) * 100));
    }

    protected override void ReadKnownState(ITreeAttribute state, IWorldAccessor world)
    {
        base.ReadKnownState(state, world);
        alongX = state.GetInt("irrigatorAlongX", 0) != 0;
        supportMask = Math.Clamp(
            state.GetInt("irrigatorSupportMask", NegativeSupport | PositiveSupport), 0, 3);
        supportPlankCode = state.GetString("irrigatorSupportPlank", DefaultSupportPlank);
        if (string.IsNullOrWhiteSpace(supportPlankCode)) supportPlankCode = DefaultSupportPlank;
    }

    protected override void WriteKnownState(ITreeAttribute state)
    {
        base.WriteKnownState(state);
        state.SetInt("irrigatorAlongX", alongX ? 1 : 0);
        state.SetInt("irrigatorSupportMask", supportMask);
        state.SetString("irrigatorSupportPlank", supportPlankCode);
    }

    public override bool OnTesselation(
        ITerrainMeshPool mesher,
        ITesselatorAPI tessThreadTesselator) =>
        IrrigatorPipeMesh.AddStaticMeshes(this, mesher, tessThreadTesselator);

    private void StopIrrigatorRenderer()
    {
        if (irrigatorRenderer == null || Api is not ICoreClientAPI capi) return;
        capi.Event.UnregisterRenderer(irrigatorRenderer, EnumRenderStage.Opaque);
        irrigatorRenderer.Dispose();
        irrigatorRenderer = null;
    }

    private void UnregisterSupport()
    {
        if (!supportRegistered || Api?.Side != EnumAppSide.Server) return;
        Api.ModLoader.GetModSystem<IrrigatorSupportSystem>().Unregister(this);
        supportRegistered = false;
    }
}
