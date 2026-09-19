using Gearwright.Hydraulics;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>An inline axle with one journal shared by up to four radial devices.</summary>
public sealed class BlockLateralCrank : BlockAxle
{
    public bool AlongX => Variant["rotation"] == "we";

    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        if (byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack != null) return false;
        BEBehaviorMPLateralCrank? crank = world.BlockAccessor.GetBlockEntity(blockSel.Position)?
            .GetBehavior<BEBehaviorMPLateralCrank>();
        if (crank == null || crank.HasAttachedDevice) return false;
        if (world.Side == EnumAppSide.Server) crank.CycleJournalAngle(byPlayer);
        return true;
    }

    public override bool TryPlaceBlock(
        IWorldAccessor world,
        IPlayer byPlayer,
        ItemStack itemstack,
        BlockSelection blockSel,
        ref string failureCode)
    {
        string? connectedRotation = FindConnectedRotation(world, blockSel.Position);
        string? pumpRotation = FindPlannedPumpRotation(
            world, blockSel.Position, out int matchingPumps, out bool conflictingPumps);
        if (conflictingPumps)
        {
            failureCode = "gearwright-reciprocating-drive-misaligned-pump";
            return false;
        }
        if (connectedRotation != null && pumpRotation != null && connectedRotation != pumpRotation)
        {
            failureCode = "gearwright-reciprocating-drive-misaligned-pump";
            return false;
        }

        string rotation = connectedRotation ?? pumpRotation ??
            (BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw).Axis == EnumAxis.X ? "we" : "ns");
        // RegistryObject.CodeWithVariant() keeps only the first dash-separated code part.
        // The public code "lateral-crank" therefore needs an explicit variant path.
        AssetLocation selectedCode = new(
            Code.Domain,
            MechanicalCodes.LateralCrankVariantPath(rotation));
        Block? selected = world.GetBlock(selectedCode);
        if (selected is not BlockLateralCrank crank ||
            !crank.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode) ||
            !crank.DoPlaceBlock(world, byPlayer, blockSel, itemstack))
        {
            return false;
        }

        bool connected = false;
        foreach (BlockFacing face in FacesFor(rotation))
        {
            BlockPos neighbourPos = blockSel.Position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is not IMechanicalPowerBlock neighbour ||
                !neighbour.HasMechPowerConnectorAt(world, neighbourPos, face.Opposite, crank)) continue;
            neighbour.DidConnectAt(world, neighbourPos, face.Opposite);
            crank.WasPlaced(world, blockSel.Position, face);
            connected = true;
        }
        if (!connected) crank.WasPlaced(world, blockSel.Position, null);
        return true;
    }

    public override void OnNeighbourBlockChange(
        IWorldAccessor world,
        BlockPos pos,
        BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        world.BlockAccessor.GetBlockEntity(pos)?
            .GetBehavior<BEBehaviorMPLateralCrank>()?
            .RefreshVisualTopology();
    }

    private string? FindConnectedRotation(IWorldAccessor world, BlockPos position)
    {
        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            BlockPos neighbourPos = position.AddCopy(face);
            if (world.BlockAccessor.GetBlock(neighbourPos) is IMechanicalPowerBlock neighbour &&
                neighbour.HasMechPowerConnectorAt(world, neighbourPos, face.Opposite, this))
            {
                return face.Axis == EnumAxis.X ? "we" : "ns";
            }
        }
        return null;
    }

    private static string? FindPlannedPumpRotation(
        IWorldAccessor world,
        BlockPos position,
        out int matchingPumps,
        out bool conflicting)
    {
        string? rotation = null;
        matchingPumps = 0;
        conflicting = false;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (world.BlockAccessor.GetBlockEntity(position.AddCopy(face)) is not
                IReciprocatingDriveDevice pump ||
                pump.DriveFace != face.Opposite)
            {
                continue;
            }

            matchingPumps++;
            string candidate = pump.ShaftFace.Axis == EnumAxis.X ? "we" : "ns";
            if (rotation != null && rotation != candidate) conflicting = true;
            rotation ??= candidate;
        }
        return rotation;
    }

    internal static BlockFacing[] FacesFor(string rotation) => rotation == "we"
        ? new[] { BlockFacing.WEST, BlockFacing.EAST }
        : new[] { BlockFacing.NORTH, BlockFacing.SOUTH };
}
