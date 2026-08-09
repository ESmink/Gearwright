using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Gearwright.Hydraulics;

public sealed class BlockIrrigatorPipe : Block
{
    private const float Sixteenth = 1f / 16f;
    private static readonly AssetLocation PlaceSound = new("game:sounds/block/plate");

    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        bool alongX = PlacementAlongX(byPlayer);
        BlockFacing negative = alongX ? BlockFacing.WEST : BlockFacing.NORTH;
        BlockFacing positive = alongX ? BlockFacing.EAST : BlockFacing.SOUTH;
        if (!HasValidSupportPlan(world.BlockAccessor, blockSel.Position, alongX))
        {
            failureCode = "gearwright-irrigator-unsupported";
            return false;
        }
        if (world.Side == EnumAppSide.Server)
        {
            HydraulicNetworkSystem system =
                world.Api.ModLoader.GetModSystem<HydraulicNetworkSystem>();
            if (system.WouldPlacementJoinDifferentContents(blockSel.Position, negative) ||
                system.WouldPlacementJoinDifferentContents(blockSel.Position, positive))
            {
                failureCode = "gearwright-pipe-content-conflict";
                return false;
            }
        }
        return base.TryPlaceBlock(world, byPlayer, itemstack, blockSel, ref failureCode);
    }

    public override bool DoPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel,
        ItemStack byItemStack)
    {
        bool alongX = PlacementAlongX(byPlayer);
        if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack)) return false;
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityIrrigatorPipe pipe)
            pipe.ConfigureAxis(alongX);
        if (world.Side == EnumAppSide.Server)
        {
            world.PlaySoundAt(
                PlaceSound, blockSel.Position, 0, null,
                randomizePitch: false, range: 18, volume: .95f);
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
        renderinfo.ModelRef = ObjectCacheUtil.GetOrCreate(
            capi, "gearwright-irrigator-pipe-inventory-mesh", () =>
            {
                Vintagestory.API.Common.Shape shape = Vintagestory.API.Common.Shape.TryGet(
                    capi, "gearwright:shapes/block/irrigator-pipe-inventory.json");
                capi.Tesselator.TesselateShape(this, shape, out MeshData mesh, new Vec3f());
                return capi.Render.UploadMultiTextureMesh(mesh);
            });
    }

    private static bool PlacementAlongX(IPlayer byPlayer)
        => BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Axis == EnumAxis.X;

    private static bool HasValidSupportPlan(
        IBlockAccessor blockAccessor,
        BlockPos position,
        bool alongX)
    {
        BlockFacing negative = alongX ? BlockFacing.WEST : BlockFacing.NORTH;
        BlockFacing positive = alongX ? BlockFacing.EAST : BlockFacing.SOUTH;
        List<BlockEntityIrrigatorPipe> before = CollectRun(
            blockAccessor, position, negative, alongX);
        before.Reverse();
        List<BlockEntityIrrigatorPipe> after = CollectRun(
            blockAccessor, position, positive, alongX);

        List<bool> eligible = new(before.Count + 1 + after.Count);
        eligible.AddRange(before.ConvertAll(pipe => HasCeiling(blockAccessor, pipe.Pos)));
        eligible.Add(HasCeiling(blockAccessor, position));
        eligible.AddRange(after.ConvertAll(pipe => HasCeiling(blockAccessor, pipe.Pos)));
        return IrrigatorSupportPlanner.TryPlan(
            eligible.Count, eligible, out _, out _);
    }

    private static List<BlockEntityIrrigatorPipe> CollectRun(
        IBlockAccessor blockAccessor,
        BlockPos origin,
        BlockFacing direction,
        bool alongX)
    {
        List<BlockEntityIrrigatorPipe> result = new();
        BlockPos cursor = origin.AddCopy(direction);
        while (blockAccessor.GetBlockEntity(cursor) is BlockEntityIrrigatorPipe pipe &&
               pipe.AlongX == alongX)
        {
            result.Add(pipe);
            cursor = cursor.AddCopy(direction);
        }
        return result;
    }

    private static bool HasCeiling(IBlockAccessor blockAccessor, BlockPos position) =>
        blockAccessor.IsSideSolid(
            position.X, position.Y + 1, position.Z, BlockFacing.DOWN);

    private static Cuboidf[] GetStateBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        BlockEntityIrrigatorPipe? pipe =
            blockAccessor.GetBlockEntity(pos) as BlockEntityIrrigatorPipe;
        bool alongX = pipe?.AlongX == true;
        List<Cuboidf> boxes = new()
        {
            alongX ? Box(0, 6, 6.25f, 16, 10, 9.75f) : Box(6.25f, 6, 0, 9.75f, 10, 16)
        };
        int mask = pipe?.SupportMask ?? 3;
        if ((mask & BlockEntityIrrigatorPipe.NegativeSupport) != 0)
            AddSupportBoxes(boxes, alongX, 2.35f, 3.85f);
        if ((mask & BlockEntityIrrigatorPipe.PositiveSupport) != 0)
            AddSupportBoxes(boxes, alongX, 12.15f, 13.65f);
        return boxes.ToArray();
    }

    private static void AddSupportBoxes(List<Cuboidf> boxes, bool alongX, float start, float end)
    {
        if (!alongX)
        {
            boxes.Add(Box(4.25f, 14.2f, start, 11.75f, 16, end));
            boxes.Add(Box(5, 6, start, 6.25f, 14.2f, end));
            boxes.Add(Box(9.75f, 6, start, 11, 14.2f, end));
            boxes.Add(Box(5, 5, start, 11, 6.2f, end));
        }
        else
        {
            boxes.Add(Box(start, 14.2f, 4.25f, end, 16, 11.75f));
            boxes.Add(Box(start, 6, 5, end, 14.2f, 6.25f));
            boxes.Add(Box(start, 6, 9.75f, end, 14.2f, 11));
            boxes.Add(Box(start, 5, 5, end, 6.2f, 11));
        }
    }

    private static Cuboidf Box(float x1, float y1, float z1, float x2, float y2, float z2) =>
        new(x1 * Sixteenth, y1 * Sixteenth, z1 * Sixteenth,
            x2 * Sixteenth, y2 * Sixteenth, z2 * Sixteenth);
}
