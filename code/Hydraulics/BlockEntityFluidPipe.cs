using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public sealed class BlockEntityFluidPipe : BlockEntityHydraulicNode
{
    private readonly HydraulicFaceAddon[] addons = new HydraulicFaceAddon[BlockFacing.NumberOfFaces];
    private readonly ItemStack?[] addonStacks = new ItemStack?[BlockFacing.NumberOfFaces];
    private HydraulicPipeRenderer? renderer;

    public bool HasSprinkler => addons[BlockFacing.DOWN.Index] == HydraulicFaceAddon.Sprinkler;

    public override bool CanConnect(BlockFacing face) => addons[face.Index] == HydraulicFaceAddon.None;

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

    public bool IsConnected(BlockFacing face)
    {
        if (!CanConnect(face) || Api == null) return false;
        BlockPos otherPos = Pos.AddCopy(face);
        return Api.World.BlockAccessor.GetBlockEntity(otherPos) is IHydraulicNetworkNode other &&
            other.CanConnect(face.Opposite);
    }

    public bool TryInstallAddon(BlockFacing face, ItemStack stack, bool sprinkler, IPlayer byPlayer)
    {
        if (!CanWriteState || addons[face.Index] != HydraulicFaceAddon.None || IsConnected(face)) return false;
        if (sprinkler && face != BlockFacing.DOWN) return false;

        addons[face.Index] = sprinkler ? HydraulicFaceAddon.Sprinkler : HydraulicFaceAddon.GlassWindow;
        addonStacks[face.Index] = stack.Clone();
        addonStacks[face.Index]!.StackSize = 1;
        MarkHydraulicsDirty(true);
        Api.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
        return true;
    }

    public bool TryRemoveAddon(BlockFacing face, IPlayer byPlayer)
    {
        if (!CanWriteState || addons[face.Index] == HydraulicFaceAddon.None) return false;
        ItemStack? returned = addonStacks[face.Index]?.Clone();
        addons[face.Index] = HydraulicFaceAddon.None;
        addonStacks[face.Index] = null;

        if (returned != null && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative &&
            !byPlayer.InventoryManager.TryGiveItemstack(returned, true))
        {
            Api.World.SpawnItemEntity(returned, Pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }

        MarkHydraulicsDirty(true);
        Api.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
        return true;
    }

    public override void OnBlockBroken(IPlayer byPlayer)
    {
        if (Api?.Side == EnumAppSide.Server)
        {
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
        if (HasSprinkler)
        {
            dsc.AppendLine(Lang.Get("gearwright:sprinkler-pressure", CurrentPressure));
            dsc.AppendLine(Lang.Get("gearwright:sprinkler-reach", HydraulicMath.Reach(CurrentPressure)));
        }
        if (CurrentLiquidCode != null)
        {
            dsc.AppendLine(Lang.Get("gearwright:pipe-liquid", CurrentLiquidCode));
        }
        dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + NetworkStatusCode));
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
        }
    }

    protected override void WriteKnownState(ITreeAttribute state)
    {
        for (int i = 0; i < addons.Length; i++)
        {
            state.SetInt("addon-" + i, (int)addons[i]);
            if (addonStacks[i] == null) state.RemoveAttribute("addonStack-" + i);
            else state.SetItemstack("addonStack-" + i, addonStacks[i]);
        }
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
}
