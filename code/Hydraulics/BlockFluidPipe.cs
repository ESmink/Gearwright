using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Gearwright.Hydraulics;

public sealed class BlockFluidPipe : Block
{
    private const float Sixteenth = 1f / 16f;
    private static readonly AssetLocation PipePlaceSound =
        new("game:sounds/block/plate");

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityFluidPipe pipe)
        {
            return false;
        }

        ItemSlot slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        ItemStack? held = slot.Itemstack;
        bool togglingPort = IsWrench(held);
        bool removing = byPlayer.Entity.Controls.ShiftKey && held == null;
        bool installingSprinkler = held?.Collectible.Code.Equals(
            new AssetLocation(GearwrightModSystem.ModId, HydraulicCodes.SprinklerItem)) == true;
        bool installingWindow = held?.Block?.Code.Equals(new AssetLocation("game", "glass-plain")) == true;
        bool installingNozzle = held?.Collectible.Code.Equals(
            new AssetLocation(GearwrightModSystem.ModId, HydraulicCodes.PipeNozzleItem)) == true;
        bool installingFlange = held?.Collectible.Code.Equals(
            new AssetLocation("game", "metalplate-copper")) == true;
        HydraulicFaceAddon addon = installingSprinkler
            ? HydraulicFaceAddon.Sprinkler
            : installingWindow
                ? HydraulicFaceAddon.GlassWindow
                : installingNozzle
                    ? HydraulicFaceAddon.PipeNozzle
                    : installingFlange
                        ? HydraulicFaceAddon.CopperFlange
                        : HydraulicFaceAddon.None;

        if (!togglingPort && !removing && addon == HydraulicFaceAddon.None) return false;
        if (world.Side == EnumAppSide.Client) return true;
        if (!world.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return true;

        bool changed = togglingPort
            ? pipe.TryTogglePort(blockSel.Face, byPlayer)
            : removing
                ? pipe.TryRemoveAddon(blockSel.Face, byPlayer)
                : pipe.TryInstallAddon(blockSel.Face, held!, addon, byPlayer);

        if (changed && !togglingPort && !removing &&
            byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }

        return true;
    }

    public override Cuboidf[] GetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        GetStateBoxes(blockAccessor, pos);

    public override Cuboidf[] GetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        GetStateBoxes(blockAccessor, pos);

    public override Cuboidf[] GetParticleCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos) =>
        GetStateBoxes(blockAccessor, pos);

    public override void OnBeforeRender(
        ICoreClientAPI capi,
        ItemStack itemstack,
        EnumItemRenderTarget target,
        ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(capi, "gearwright-fluid-pipe-inventory-mesh", () =>
        {
            Vintagestory.API.Common.Shape shape = Vintagestory.API.Common.Shape.TryGet(
                capi, "gearwright:shapes/block/fluid-pipe-inventory.json");
            capi.Tesselator.TesselateShape(this, shape, out MeshData mesh, new Vec3f());
            return capi.Render.UploadMultiTextureMesh(mesh);
        });
    }

    private static Cuboidf[] GetStateBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        List<Cuboidf> boxes = new()
        {
            Box(6, 6, 6, 10, 10, 10)
        };

        if (blockAccessor.GetBlockEntity(pos) is not BlockEntityFluidPipe pipe) return boxes.ToArray();

        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.IsPortEnabled(face) || pipe.GetAddon(face) == HydraulicFaceAddon.CopperFlange)
                AddConnectionBoxes(boxes, face);
            else if (pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle)
                AddIntakeBox(boxes, face);
        }

        if (pipe.HasSprinkler)
        {
            boxes.Add(Box(4.5f, 0.35f, 5.25f, 11.5f, 6, 10.75f));
        }

        return boxes.ToArray();
    }

    private static void AddConnectionBoxes(List<Cuboidf> boxes, BlockFacing face)
    {
        if (face == BlockFacing.NORTH)
        {
            boxes.Add(Box(6, 6, 0, 10, 10, 6));
            boxes.Add(Box(5.5f, 5.5f, 0, 10.5f, 10.5f, 0.65f));
        }
        else if (face == BlockFacing.SOUTH)
        {
            boxes.Add(Box(6, 6, 10, 10, 10, 16));
            boxes.Add(Box(5.5f, 5.5f, 15.35f, 10.5f, 10.5f, 16));
        }
        else if (face == BlockFacing.WEST)
        {
            boxes.Add(Box(0, 6, 6, 6, 10, 10));
            boxes.Add(Box(0, 5.5f, 5.5f, 0.65f, 10.5f, 10.5f));
        }
        else if (face == BlockFacing.EAST)
        {
            boxes.Add(Box(10, 6, 6, 16, 10, 10));
            boxes.Add(Box(15.35f, 5.5f, 5.5f, 16, 10.5f, 10.5f));
        }
        else if (face == BlockFacing.DOWN)
        {
            boxes.Add(Box(6, 0, 6, 10, 6, 10));
            boxes.Add(Box(5.5f, 0, 5.5f, 10.5f, 0.65f, 10.5f));
        }
        else if (face == BlockFacing.UP)
        {
            boxes.Add(Box(6, 10, 6, 10, 16, 10));
            boxes.Add(Box(5.5f, 15.35f, 5.5f, 10.5f, 16, 10.5f));
        }
    }

    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        if (world.Side == EnumAppSide.Server &&
            world.Api.ModLoader.GetModSystem<HydraulicNetworkSystem>()
                .WouldPlacementJoinDifferentContents(
                    blockSel.Position, InitialPortFace(byPlayer, blockSel)))
        {
            failureCode = "gearwright-pipe-content-conflict";
            return false;
        }
        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ItemStack byItemStack)
    {
        BlockFacing initialFace = InitialPortFace(byPlayer, blockSel);
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack)) return false;
        if (world.Side == EnumAppSide.Server)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityFluidPipe pipe)
            {
                pipe.ConfigurePlacedPort(initialFace);
            }
            world.PlaySoundAt(
                PipePlaceSound, blockSel.Position, 0, null,
                randomizePitch: false, range: 18, volume: 0.95f);
        }
        return true;
    }

    private static BlockFacing InitialPortFace(IPlayer byPlayer, BlockSelection blockSel) =>
        byPlayer.Entity.Controls.ShiftKey ? blockSel.Face : blockSel.Face.Opposite;

    private static bool IsWrench(ItemStack? stack) =>
        stack?.Collectible.Code.Domain == "game" &&
        stack.Collectible.Code.Path.StartsWith("wrench-", System.StringComparison.Ordinal);

    private static void AddIntakeBox(List<Cuboidf> boxes, BlockFacing face)
    {
        const float crossMin = 14f / 3f;
        const float crossMax = 34f / 3f;
        if (face == BlockFacing.NORTH)
            boxes.Add(Box(crossMin, crossMin, -2.5f, crossMax, crossMax, 6));
        else if (face == BlockFacing.SOUTH)
            boxes.Add(Box(crossMin, crossMin, 10, crossMax, crossMax, 18.5f));
        else if (face == BlockFacing.WEST)
            boxes.Add(Box(-2.5f, crossMin, crossMin, 6, crossMax, crossMax));
        else if (face == BlockFacing.EAST)
            boxes.Add(Box(10, crossMin, crossMin, 18.5f, crossMax, crossMax));
        else if (face == BlockFacing.DOWN)
            boxes.Add(Box(crossMin, -2.5f, crossMin, crossMax, 6, crossMax));
        else if (face == BlockFacing.UP)
            boxes.Add(Box(crossMin, 10, crossMin, crossMax, 18.5f, crossMax));
    }

    private static Cuboidf Box(float x1, float y1, float z1, float x2, float y2, float z2) =>
        new(x1 * Sixteenth, y1 * Sixteenth, z1 * Sixteenth,
            x2 * Sixteenth, y2 * Sixteenth, z2 * Sixteenth);
}
