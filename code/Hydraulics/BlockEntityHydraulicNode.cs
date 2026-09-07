using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public abstract class BlockEntityHydraulicNode : BlockEntity, IHydraulicNetworkNode
{
    protected const string StorageKey = "gearwrightHydraulics";

    private ITreeAttribute preservedState = new TreeAttribute();
    private bool canWrite = true;
    private bool registered;
    private bool pendingSimulationUpdate;
    private long lastSimulationUpdateMilliseconds;

    public BlockPos Position => Pos;
    public bool CanWriteState => canWrite;
    public AssetLocation? CurrentContentCode { get; private set; }
    public double CurrentPressure { get; private set; }
    public string NetworkStatusCode { get; private set; } = "idle";
    public BlockFacing? CurrentFlowDirection { get; private set; }

    public abstract bool CanConnect(BlockFacing face);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            api.ModLoader.GetModSystem<HydraulicNetworkSystem>().Register(this);
            registered = true;
        }
    }

    public override void OnBlockRemoved()
    {
        Unregister();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        Unregister();
        base.OnBlockUnloaded();
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        ITreeAttribute? state = tree.GetTreeAttribute(StorageKey);
        if (state == null)
        {
            preservedState = new TreeAttribute();
            canWrite = true;
            ReadKnownState(preservedState, worldAccessForResolve);
            return;
        }

        int? storedSchema = state.TryGetInt("schemaVersion");
        preservedState = HydraulicStateSchema.PrepareForRead(state, out canWrite, out string? problem);
        if (!canWrite)
        {
            worldAccessForResolve.Logger.Error(
                "[Gearwright] Hydraulic state at {0} has a {1} schema ({2}). The original data will remain read-only.",
                Pos, problem, storedSchema?.ToString() ?? "non-integer");
        }
        else if (storedSchema is 1 or 2 or 3 or 4 or 5 or 6)
        {
            worldAccessForResolve.Logger.Notification(
                "[Gearwright] Migrated hydraulic state at {0} from schema {1} to schema {2}.",
                Pos, storedSchema, HydraulicStateSchema.CurrentVersion);
        }

        ReadNetworkCache(preservedState);
        ReadKnownState(preservedState, worldAccessForResolve);
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
        state.SetInt("schemaVersion", HydraulicStateSchema.CurrentVersion);
        WriteNetworkCache(state);
        WriteKnownState(state);
        preservedState = state.Clone();
        tree[StorageKey] = state;
    }

    public virtual void SetNetworkState(
        AssetLocation? contentCode,
        double pressure,
        string statusCode,
        BlockFacing? flowDirection)
    {
        string? oldContent = CurrentContentCode?.ToString();
        double oldPressure = CurrentPressure;
        string oldStatus = NetworkStatusCode;
        string? oldFlowDirection = CurrentFlowDirection?.Code;
        CurrentContentCode = contentCode;
        CurrentPressure = double.IsFinite(pressure) ? pressure : 0;
        NetworkStatusCode = statusCode;
        CurrentFlowDirection = flowDirection;

        MarkSimulationDirty(oldContent != contentCode?.ToString() || oldPressure != CurrentPressure ||
            oldStatus != statusCode || oldFlowDirection != flowDirection?.Code);
    }

    protected virtual void ReadKnownState(ITreeAttribute state, IWorldAccessor world) { }
    protected virtual void WriteKnownState(ITreeAttribute state) { }

    private void ReadNetworkCache(ITreeAttribute state)
    {
        string content = state.GetString("networkContent", "");
        try { CurrentContentCode = string.IsNullOrWhiteSpace(content) ? null : new AssetLocation(content); }
        catch { CurrentContentCode = null; }
        double storedPressure = state.GetDouble("networkPressure", 0);
        CurrentPressure = double.IsFinite(storedPressure) ? storedPressure : 0;
        NetworkStatusCode = state.GetString("networkStatus", "idle");
        string flowDirection = state.GetString("networkFlowDirection", "");
        CurrentFlowDirection = string.IsNullOrWhiteSpace(flowDirection)
            ? null
            : BlockFacing.FromCode(flowDirection);
    }

    private void WriteNetworkCache(ITreeAttribute state)
    {
        if (CurrentContentCode == null) state.RemoveAttribute("networkContent");
        else state.SetString("networkContent", CurrentContentCode.ToString());
        state.SetDouble("networkPressure", CurrentPressure);
        state.SetString("networkStatus", NetworkStatusCode);
        if (CurrentFlowDirection == null) state.RemoveAttribute("networkFlowDirection");
        else state.SetString("networkFlowDirection", CurrentFlowDirection.Code);
    }

    protected void MarkHydraulicsDirty(bool redrawOnClient = false)
    {
        if (canWrite) MarkDirty(redrawOnClient);
    }

    protected void MarkSimulationDirty(bool changed)
    {
        if (!canWrite || Api?.Side != EnumAppSide.Server) return;
        pendingSimulationUpdate |= changed;
        long now = Api.World.ElapsedMilliseconds;
        if (!pendingSimulationUpdate || now - lastSimulationUpdateMilliseconds < 100) return;
        pendingSimulationUpdate = false;
        lastSimulationUpdateMilliseconds = now;
        MarkDirty(false);
    }

    private void Unregister()
    {
        if (!registered || Api?.Side != EnumAppSide.Server) return;
        Api.ModLoader.GetModSystem<HydraulicNetworkSystem>().Unregister(this);
        registered = false;
    }
}
