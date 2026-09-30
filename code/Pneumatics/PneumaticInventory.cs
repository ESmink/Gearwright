using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Gearwright.Pneumatics;

internal sealed record PneumaticInventory(BlockEntityGenericTypedContainer Chest, BEBehaviorPneumaticChest Receipt)
{
    public InventoryBase Inventory => Chest.Inventory;

    public static PneumaticInventory? At(IWorldAccessor world, BlockPos pos)
    {
        if (world.BlockAccessor.GetChunkAtBlockPos(pos) == null ||
            world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGenericTypedContainer chest ||
            chest.Block.Code.Domain != "game" || !chest.Block.Code.Path.StartsWith("chest-", StringComparison.Ordinal) ||
            chest.type != "normal-generic" || chest.Inventory.Count != 16 ||
            chest.GetBehavior<BEBehaviorPneumaticChest>() is not { Writable: true } receipt) return null;
        return new(chest, receipt);
    }

    // Native slot hooks preserve tools and their durability attributes. Items
    // that need ticking transitions or nested inventories remain unsupported.
    public static bool Supported(ItemStack stack) => stack.Collectible.TransitionableProps == null &&
        stack.Collectible is not BlockLiquidContainerBase && !stack.Attributes.HasAttribute("contents") &&
        !stack.Attributes.HasAttribute("transitionstate");

    public int Capacity(IWorldAccessor world, ItemStack stack)
    {
        if (Inventory.PutLocked) return 0;
        var source = new DummySlot(stack.Clone());
        int capacity = 0;
        for (int i = 0; i < Inventory.Count; i++)
        {
            var slot = Inventory[i];
            if (!slot.CanTakeFrom(source, EnumMergePriority.AutoMerge)) continue;
            if (!slot.Empty && !slot.Itemstack.Equals(world, stack, GlobalConstants.IgnoredStackAttributes)) continue;
            capacity += Math.Max(0, slot.GetRemainingSlotSpace(stack));
        }
        return capacity;
    }

    public int Insert(IWorldAccessor world, ItemSlot source)
    {
        int moved = 0;
        // Native merge/attribute hooks decide acceptance; the remainder stays in
        // the real host slot. A sample is never assigned to the chest.
        for (int pass = 0; pass < 2 && !source.Empty; pass++)
            for (int i = 0; i < Inventory.Count && !source.Empty; i++)
                if (Inventory[i].Empty == (pass == 1))
                    moved += source.TryPutInto(world, Inventory[i], source.StackSize);
        return moved;
    }
}
