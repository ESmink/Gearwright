using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public sealed class BlockEntityFluidPipe : BlockEntityHydraulicNode
{
    private const double EmptyEpsilonLitres = 0.000001;
    private static readonly AssetLocation AttachmentInstallSound =
        new("game:sounds/block/metaldoor-place");
    private static readonly AssetLocation AttachmentRemoveSound =
        new("game:sounds/block/chute");
    private static readonly AssetLocation PipeRemoveSound =
        new("game:sounds/block/heavymetal-hit");
    private readonly HydraulicFaceAddon[] addons = new HydraulicFaceAddon[BlockFacing.NumberOfFaces];
    private readonly bool[] ports = new bool[BlockFacing.NumberOfFaces];
    private readonly ItemStack?[] addonStacks = new ItemStack?[BlockFacing.NumberOfFaces];
    private readonly double[] nozzleFlowRates = new double[BlockFacing.NumberOfFaces];
    private readonly double[] nozzleItemRemainders = new double[BlockFacing.NumberOfFaces];
    private double contentAmountLitres;
    private double contentTemperatureC = 20;
    private double throughputLitresPerSecond;
    private double lastSimulationTotalHours = -1;
    private HydraulicPipeRenderer? renderer;

    public bool HasSprinkler => addons[BlockFacing.DOWN.Index] == HydraulicFaceAddon.Sprinkler;
    public double ContentAmountLitres => contentAmountLitres;
    public double ContentTemperatureC => contentTemperatureC;
    public double ThroughputLitresPerSecond => throughputLitresPerSecond;
    public double LastSimulationTotalHours => lastSimulationTotalHours;
    public bool HasContent => CurrentContentCode != null && contentAmountLitres > EmptyEpsilonLitres;
    public PipeContentPhase ContentPhase => CurrentContentCode != null && PipeContent.IsSteam(CurrentContentCode)
        ? PipeContentPhase.Gas
        : PipeContentPhase.Liquid;
    public double FillFraction => ContentPhase == PipeContentPhase.Gas
        ? 1 - Math.Exp(-Math.Max(0, contentAmountLitres) / HydraulicMath.PipeCapacityLitres)
        : Math.Clamp(contentAmountLitres / HydraulicMath.PipeCapacityLitres, 0, 1);

    public override bool CanConnect(BlockFacing face) =>
        ports[face.Index] && addons[face.Index] == HydraulicFaceAddon.None;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is ICoreClientAPI capi)
        {
            renderer = new HydraulicPipeRenderer(this, capi);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-hydraulic-pipe");
        }
    }

    public HydraulicFaceAddon GetAddon(BlockFacing face) => addons[face.Index];

    public bool IsPortEnabled(BlockFacing face) =>
        ports[face.Index] && addons[face.Index] == HydraulicFaceAddon.None;

    public bool IsPortOpenToAir(BlockFacing face)
    {
        if (!IsPortEnabled(face) || IsConnected(face) || Api == null) return false;
        return Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face)).BlockMaterial == EnumBlockMaterial.Air;
    }

    public bool IsConnected(BlockFacing face)
    {
        if (!CanConnect(face) || Api == null) return false;
        BlockPos otherPos = Pos.AddCopy(face);
        return Api.World.BlockAccessor.GetBlockEntity(otherPos) is IHydraulicNetworkNode other &&
            other.CanConnect(face.Opposite);
    }

    public void ConfigurePlacedPort(BlockFacing initialFace)
    {
        if (!CanWriteState) return;
        Array.Clear(ports, 0, ports.Length);
        if (addons[initialFace.Index] == HydraulicFaceAddon.None)
        {
            ports[initialFace.Index] = true;
        }

        if (Api?.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(initialFace)) is BlockEntityFluidPipe neighbor)
        {
            neighbor.EnableReciprocalPort(initialFace.Opposite);
        }
        MarkPortStateDirty(initialFace);
    }

    public bool TryTogglePort(BlockFacing face, IPlayer byPlayer)
    {
        if (!CanWriteState || addons[face.Index] != HydraulicFaceAddon.None) return false;
        bool enabling = !ports[face.Index];
        if (enabling && Api?.Side == EnumAppSide.Server &&
            Api.ModLoader.GetModSystem<HydraulicNetworkSystem>().WouldJoinDifferentContents(this, face))
        {
            if (byPlayer is Vintagestory.API.Server.IServerPlayer serverPlayer)
            {
                serverPlayer.SendIngameError(
                    "gearwright-pipe-content-conflict",
                    Lang.Get("gearwright:pipe-content-conflict"));
            }
            return false;
        }

        ports[face.Index] = enabling;
        MarkPortStateDirty(face);
        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.PlaySoundAt(
                new AssetLocation("game:sounds/block/metaldoor"), Pos, 0, null,
                randomizePitch: false, range: 14, volume: 0.8f);
        }
        return true;
    }

    public bool TryInstallAddon(
        BlockFacing face,
        ItemStack stack,
        HydraulicFaceAddon addon,
        IPlayer byPlayer)
    {
        if (!CanWriteState || addons[face.Index] != HydraulicFaceAddon.None || IsConnected(face)) return false;
        if (addon != HydraulicFaceAddon.GlassWindow &&
            addon != HydraulicFaceAddon.Sprinkler &&
            addon != HydraulicFaceAddon.PipeNozzle) return false;
        if (addon == HydraulicFaceAddon.Sprinkler && face != BlockFacing.DOWN) return false;

        addons[face.Index] = addon;
        ports[face.Index] = false;
        addonStacks[face.Index] = stack.Clone();
        addonStacks[face.Index]!.StackSize = 1;
        if (Api.Side == EnumAppSide.Server)
        {
            Api.World.PlaySoundAt(
                AttachmentInstallSound, Pos, 0, null, randomizePitch: false, range: 14, volume: 0.9f);
        }
        MarkHydraulicsDirty(true);
        Api.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
        return true;
    }

    public bool TryRemoveAddon(BlockFacing face, IPlayer byPlayer)
    {
        if (!CanWriteState || addons[face.Index] == HydraulicFaceAddon.None) return false;
        if (Api?.Side == EnumAppSide.Server &&
            Api.ModLoader.GetModSystem<HydraulicNetworkSystem>().WouldJoinDifferentContents(this, face))
        {
            if (byPlayer is Vintagestory.API.Server.IServerPlayer serverPlayer)
            {
                serverPlayer.SendIngameError(
                    "gearwright-pipe-content-conflict",
                    Lang.Get("gearwright:pipe-content-conflict"));
            }
            return false;
        }
        ItemStack? returned = addonStacks[face.Index]?.Clone();
        addons[face.Index] = HydraulicFaceAddon.None;
        addonStacks[face.Index] = null;
        ports[face.Index] = true;

        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.PlaySoundAt(
                AttachmentRemoveSound, Pos, 0, null, randomizePitch: false, range: 14, volume: 0.9f);
        }

        if (returned != null && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative &&
            !byPlayer.InventoryManager.TryGiveItemstack(returned, true))
        {
            Api!.World.SpawnItemEntity(returned, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        MarkHydraulicsDirty(true);
        Api!.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
        return true;
    }

    public override void OnBlockBroken(IPlayer byPlayer)
    {
        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.PlaySoundAt(
                PipeRemoveSound, Pos, 0, null, randomizePitch: false, range: 18, volume: 0.95f);
            for (int i = 0; i < addonStacks.Length; i++)
            {
                ItemStack? stack = addonStacks[i];
                if (stack != null &&
                    (byPlayer == null || byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative))
                {
                    Api.World.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }
        }
        StopRenderer();
        base.OnBlockBroken(byPlayer);
    }

    public override void OnBlockRemoved()
    {
        StopRenderer();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopRenderer();
        base.OnBlockUnloaded();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        List<string> openPorts = new();
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (IsPortEnabled(face)) openPorts.Add(face.Code);
        }
        dsc.AppendLine(Lang.Get(
            "gearwright:pipe-open-ports",
            openPorts.Count == 0 ? Lang.Get("gearwright:pipe-open-ports-none") : string.Join(", ", openPorts)));
        if (HasSprinkler)
        {
            dsc.AppendLine(Lang.Get("gearwright:sprinkler-pressure", CurrentPressure));
            dsc.AppendLine(Lang.Get("gearwright:sprinkler-reach", HydraulicMath.Reach(CurrentPressure)));
        }
        if (CurrentContentCode != null)
        {
            string contentName = PipeContent.IsSteam(CurrentContentCode)
                ? Lang.Get("gearwright:pipe-content-steam")
                : CurrentContentCode.ToString();
            dsc.AppendLine(Lang.Get("gearwright:pipe-content", contentName));
            dsc.AppendLine(Lang.Get("gearwright:pipe-amount", contentAmountLitres));
            dsc.AppendLine(Lang.Get("gearwright:pipe-temperature", contentTemperatureC));
        }
        dsc.AppendLine(Lang.Get("gearwright:pipe-pressure", CurrentPressure));
        dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + NetworkStatusCode));
    }

    public double GetNozzleFlowRate(BlockFacing face) => nozzleFlowRates[face.Index];

    public double GetNozzleItemRemainder(BlockFacing face) => nozzleItemRemainders[face.Index];

    public void SetNozzleItemRemainder(BlockFacing face, double value)
    {
        double next = Math.Clamp(
            double.IsFinite(value) ? value : 0, 0, 0.999999);
        if (Math.Abs(nozzleItemRemainders[face.Index] - next) < 0.000001) return;
        nozzleItemRemainders[face.Index] = next;
        if (Api?.Side == EnumAppSide.Server) MarkHydraulicsDirty();
    }

    public void ApplySimulationState(
        AssetLocation? contentCode,
        double amountLitres,
        double temperatureC,
        double pressureKPa,
        string statusCode,
        BlockFacing? flowDirection,
        double throughput,
        IReadOnlyList<double> nozzleFlows,
        double totalHours)
    {
        double nextAmount = Math.Max(0, double.IsFinite(amountLitres) ? amountLitres : 0);
        if (contentCode != null && PipeContent.Phase(Api.World, contentCode) == PipeContentPhase.Liquid)
        {
            nextAmount = Math.Min(HydraulicMath.PipeCapacityLitres, nextAmount);
        }
        double nextTemperature = double.IsFinite(temperatureC)
            ? temperatureC
            : contentCode == null ? 20 : PipeContent.DefaultTemperatureC(contentCode);
        bool changed = Math.Abs(contentAmountLitres - nextAmount) >= 0.001 ||
            Math.Abs(contentTemperatureC - nextTemperature) >= 0.1 ||
            Math.Abs(throughputLitresPerSecond - throughput) >= 0.01;

        contentAmountLitres = nextAmount;
        contentTemperatureC = nextTemperature;
        throughputLitresPerSecond = Math.Max(0, double.IsFinite(throughput) ? throughput : 0);
        lastSimulationTotalHours = double.IsFinite(totalHours) ? totalHours : lastSimulationTotalHours;
        for (int i = 0; i < nozzleFlowRates.Length; i++)
        {
            double next = i < nozzleFlows.Count && double.IsFinite(nozzleFlows[i]) ? nozzleFlows[i] : 0;
            if (Math.Abs(nozzleFlowRates[i] - next) >= 0.01) changed = true;
            nozzleFlowRates[i] = next;
        }

        SetNetworkState(contentCode, pressureKPa, statusCode, flowDirection);
        if (changed && Api?.Side == EnumAppSide.Server) MarkHydraulicsDirty();
    }

    protected override void ReadKnownState(ITreeAttribute state, IWorldAccessor world)
    {
        for (int i = 0; i < addons.Length; i++)
        {
            int value = state.GetInt("addon-" + i, 0);
            addons[i] = Enum.IsDefined(typeof(HydraulicFaceAddon), value)
                ? (HydraulicFaceAddon)value
                : HydraulicFaceAddon.None;
            addonStacks[i] = state.GetItemstack("addonStack-" + i, null);
            addonStacks[i]?.ResolveBlockOrItem(world);
            nozzleFlowRates[i] = state.GetDouble("nozzleFlow-" + i, 0);
            nozzleItemRemainders[i] = Math.Clamp(state.GetDouble("nozzleRemainder-" + i, 0), 0, 0.999999);
            ports[i] = state.GetInt("port-" + i, 0) != 0;
        }
        contentAmountLitres = Math.Max(0, state.GetDouble("contentAmountLitres", 0));
        contentTemperatureC = state.GetDouble("contentTemperatureC", 20);
        throughputLitresPerSecond = Math.Max(0, state.GetDouble("throughputLitresPerSecond", 0));
        lastSimulationTotalHours = state.GetDouble("lastSimulationTotalHours", -1);
    }

    protected override void WriteKnownState(ITreeAttribute state)
    {
        for (int i = 0; i < addons.Length; i++)
        {
            state.SetInt("addon-" + i, (int)addons[i]);
            if (addonStacks[i] == null) state.RemoveAttribute("addonStack-" + i);
            else state.SetItemstack("addonStack-" + i, addonStacks[i]);
            state.SetDouble("nozzleFlow-" + i, nozzleFlowRates[i]);
            state.SetDouble("nozzleRemainder-" + i, nozzleItemRemainders[i]);
            state.SetInt("port-" + i, ports[i] ? 1 : 0);
        }
        state.SetDouble("contentAmountLitres", contentAmountLitres);
        state.SetDouble("contentTemperatureC", contentTemperatureC);
        state.SetDouble("throughputLitresPerSecond", throughputLitresPerSecond);
        state.SetDouble("lastSimulationTotalHours", lastSimulationTotalHours);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator) =>
        HydraulicPipeMesh.AddStaticMeshes(this, mesher, tessThreadTesselator);

    private void StopRenderer()
    {
        if (renderer == null || Api is not ICoreClientAPI capi) return;
        capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
        renderer.Dispose();
        renderer = null;
    }

    private void EnableReciprocalPort(BlockFacing face)
    {
        if (!CanWriteState || addons[face.Index] != HydraulicFaceAddon.None || ports[face.Index]) return;
        ports[face.Index] = true;
        MarkPortStateDirty(face);
    }

    private void MarkPortStateDirty(BlockFacing face)
    {
        MarkHydraulicsDirty(true);
        Api?.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        Api?.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
    }
}
