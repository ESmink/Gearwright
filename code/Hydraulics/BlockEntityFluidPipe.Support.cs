using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public partial class BlockEntityFluidPipe
{
    private ItemStack? woodSupportStack;
    private ItemStack? woodInsulationStack;
    public bool HasWoodSupport => woodSupportStack != null;
    public bool HasWoodInsulation => HasWoodSupport && woodInsulationStack != null;

    internal static bool IsSupportPlank(ItemStack? stack) =>
        stack?.Item != null && stack.StackSize > 0 && stack.Item.Code.Domain == "game" &&
        stack.Item.Code.Path.StartsWith("plank-", StringComparison.Ordinal);

    internal bool HasSupportEndpoint(BlockFacing face) => IsPortEnabled(face) ||
        GetAddon(face) is HydraulicFaceAddon.PipeNozzle or HydraulicFaceAddon.CopperFlange or HydraulicFaceAddon.Sprinkler;

    internal bool HasPipeAbove(IBlockAccessor accessor) =>
        accessor.GetBlockEntity(Pos.AddCopy(BlockFacing.UP)) is BlockEntityFluidPipe;

    internal bool HasSupportPlatform(IBlockAccessor accessor) => HasWoodSupport && !HasPipeAbove(accessor);

    internal bool TryInstallWoodSupport(ItemStack stack)
    {
        if (Api?.Side != EnumAppSide.Server || !CanWriteState || HasWoodSupport ||
            Block is not BlockFluidPipe || !IsSupportPlank(stack)) return false;
        woodSupportStack = stack.Clone();
        woodSupportStack.StackSize = 1;
        WoodSupportChanged();
        return true;
    }

    internal bool TryRemoveWoodSupport(IPlayer player)
    {
        if (Api?.Side != EnumAppSide.Server || !CanWriteState || woodSupportStack == null || HasWoodInsulation) return false;
        ItemStack returned = woodSupportStack.Clone();
        woodSupportStack = null;
        WoodSupportChanged();
        ReturnSupportPlank(returned, player);
        return true;
    }

    internal bool TryInstallWoodInsulation(ItemStack stack)
    {
        if (Api?.Side != EnumAppSide.Server || !CanWriteState || !HasWoodSupport ||
            HasWoodInsulation || Block is not BlockFluidPipe || !IsSupportPlank(stack)) return false;
        woodInsulationStack = stack.Clone();
        woodInsulationStack.StackSize = 1;
        WoodSupportChanged();
        Api.World.BlockAccessor.MarkAbsorptionChanged(Block.LightAbsorption, 32, Pos);
        InvalidateInsulationRoom();
        return true;
    }

    internal bool TryRemoveWoodInsulation(IPlayer player)
    {
        if (Api?.Side != EnumAppSide.Server || !CanWriteState || !HasWoodInsulation) return false;
        ItemStack returned = woodInsulationStack!.Clone();
        woodInsulationStack = null;
        WoodSupportChanged();
        Api.World.BlockAccessor.MarkAbsorptionChanged(32, Block.LightAbsorption, Pos);
        InvalidateInsulationRoom();
        ReturnSupportPlank(returned, player);
        return true;
    }

    private void ReturnSupportPlank(ItemStack returned, IPlayer player)
    {
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative &&
            !player.InventoryManager.TryGiveItemstack(returned, true))
            Api.World.SpawnItemEntity(returned, Pos.ToVec3d().Add(.5, .5, .5));
    }

    private void InvalidateInsulationRoom()
    {
        // A block-entity redraw only queues a client update. The room registry
        // needs the engine's chunk-change event when this block's seal changes.
        // The 1.22.3 publisher is an internal engine class, so bind its existing
        // event rather than replacing the room registry or modifying its cache.
        if (Api.Event is not { } events || Api.World.BlockAccessor.GetChunkAtBlockPos(Pos) is not { } chunk) return;
        MethodInfo? trigger = events.GetType().GetMethod("TriggerChunkDirty",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Vec3i), typeof(IWorldChunk), typeof(EnumChunkDirtyReason) }, null);
        if (trigger == null)
        {
            Api.World.Logger.Error("[Gearwright] The engine room-update hook is unavailable at {0}; cached rooms may be stale.", Pos);
            return;
        }
        trigger.Invoke(events, new object[] {
            new Vec3i(Pos.X / GlobalConstants.ChunkSize, Pos.InternalY / GlobalConstants.ChunkSize,
                Pos.Z / GlobalConstants.ChunkSize), chunk, EnumChunkDirtyReason.MarkedDirty });
    }

    private void WoodSupportChanged()
    {
        MarkHydraulicsDirty(true);
        Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), Pos, 0, null,
            randomizePitch: false, range: 14, volume: .8f);
    }

    private void DropWoodSupport(IPlayer? player)
    {
        bool wasInsulated = HasWoodInsulation;
        ItemStack? frame = woodSupportStack;
        ItemStack? lining = woodInsulationStack;
        woodSupportStack = null;
        woodInsulationStack = null;
        if (wasInsulated) Api.World.BlockAccessor.MarkAbsorptionChanged(32, Block.LightAbsorption, Pos);
        if (player != null && player.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
        foreach (ItemStack? stack in new[] { frame, lining })
            if (stack != null) Api.World.SpawnItemEntity(stack.Clone(), Pos.ToVec3d().Add(.5, .5, .5));
    }

    private void ReadWoodSupport(ITreeAttribute state, IWorldAccessor world)
    {
        bool wasInsulated = HasWoodInsulation;
        woodSupportStack = ReadSupportPlank(state, world, "woodSupportStack");
        woodInsulationStack = ReadSupportPlank(state, world, "woodInsulationStack");
        if (woodInsulationStack != null && woodSupportStack == null)
            ProtectStoredState(world, "wooden insulation without its support frame");
        if (Api?.Side == EnumAppSide.Client && wasInsulated != HasWoodInsulation)
        {
            Api.World.BlockAccessor.MarkAbsorptionChanged(wasInsulated ? 32 : Block.LightAbsorption,
                HasWoodInsulation ? 32 : Block.LightAbsorption, Pos);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }

    private ItemStack? ReadSupportPlank(ITreeAttribute state, IWorldAccessor world, string key)
    {
        if (!state.HasAttribute(key)) return null;
        if (state[key] is not ItemstackAttribute)
        {
            ProtectStoredState(world, "invalid wooden support attribute " + key);
            return null;
        }
        ItemStack? saved = state.GetItemstack(key);
        ItemStack? resolved = saved?.Clone();
        if (resolved == null || !resolved.ResolveBlockOrItem(world) || resolved.StackSize != 1 || !IsSupportPlank(resolved))
        {
            ProtectStoredState(world, "invalid or unresolved wooden support stack " + key);
            return null;
        }
        return resolved;
    }

    private void WriteWoodSupport(ITreeAttribute state)
    {
        if (woodSupportStack == null) state.RemoveAttribute("woodSupportStack");
        else state.SetItemstack("woodSupportStack", woodSupportStack.Clone());
        if (woodInsulationStack == null) state.RemoveAttribute("woodInsulationStack");
        else state.SetItemstack("woodInsulationStack", woodInsulationStack.Clone());
    }
}
