using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>Intercepts Vanilla clutch actions so they never merge transmission networks.</summary>
public sealed class BlockControlledClutch : BlockClutch
{
    public override bool OnBlockInteractStart(
        IWorldAccessor world,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        return world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEControlledClutch clutch &&
            clutch.Toggle(byPlayer);
    }

    public override void Activate(
        IWorldAccessor world,
        Caller caller,
        BlockSelection blockSel,
        ITreeAttribute activationArgs)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEControlledClutch clutch) return;
        bool target = activationArgs.HasAttribute("engaged")
            ? activationArgs.GetBool("engaged")
            : !clutch.Engaged;
        clutch.SetControlledEngaged(target, null);
    }

    public override void OnNeighbourBlockChange(
        IWorldAccessor world,
        BlockPos pos,
        BlockPos neibpos)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is BEControlledClutch clutch)
        {
            clutch.HandleNeighbourChange(neibpos);
        }
    }
}
