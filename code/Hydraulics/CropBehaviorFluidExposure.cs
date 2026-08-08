using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

/// <summary>Converts a sprayed crop on its next normal growth attempt.</summary>
public sealed class CropBehaviorFluidExposure : CropBehavior
{
    public CropBehaviorFluidExposure(Block block) : base(block) { }

    public override void OnPlanted(ICoreAPI api, ItemSlot itemslot, EntityAgent byEntity, BlockSelection blockSel)
    {
        base.OnPlanted(api, itemslot, byEntity, blockSel);
        BlockEntityFarmland? farmland = api.World.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityFarmland
            ?? api.World.BlockAccessor.GetBlockEntity(blockSel.Position.DownCopy()) as BlockEntityFarmland;
        if (farmland == null) return;
        ITreeAttribute? exposure = farmland.CropAttributes.GetTreeAttribute(HydraulicCodes.FarmlandExposureTree);
        if (exposure?.TryGetInt("schemaVersion") == 1)
        {
            farmland.CropAttributes.RemoveAttribute(HydraulicCodes.FarmlandExposureTree);
            farmland.MarkDirty(false);
        }
    }

    public override bool TryGrowCrop(
        ICoreAPI api,
        IFarmlandBlockEntity farmland,
        double currentTotalHours,
        int newGrowthStage,
        ref EnumHandling handling)
    {
        ITreeAttribute? exposure = farmland.CropAttributes.GetTreeAttribute(HydraulicCodes.FarmlandExposureTree);
        if (exposure?.TryGetInt("schemaVersion") != 1) return true;

        BlockPos cropPos = farmland.UpPos.Copy();
        Block crop = api.World.BlockAccessor.GetBlock(cropPos);
        Block? deadCrop = api.World.GetBlock(new AssetLocation("game:deadcrop"));
        if (crop.CropProps == null || deadCrop == null || deadCrop.Id == 0) return true;

        api.World.BlockAccessor.SetBlock(deadCrop.Id, cropPos);
        if (api.World.BlockAccessor.GetBlockEntity(cropPos) is BlockEntityDeadCrop deadEntity)
        {
            deadEntity.Inventory[0].Itemstack = new ItemStack(crop, 1);
            deadEntity.deathReason = EnumCropStressType.Salt;
            deadEntity.MarkDirty(true);
        }

        farmland.CropAttributes.RemoveAttribute(HydraulicCodes.FarmlandExposureTree);
        if (api.World.BlockAccessor.GetBlockEntity(farmland.Pos) is BlockEntity blockEntity)
        {
            blockEntity.MarkDirty(false);
        }
        handling = EnumHandling.PreventDefault;
        return false;
    }
}
