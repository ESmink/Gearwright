using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>Vanilla-rendered clutch whose state controls Gearwright's coupling boundary.</summary>
public sealed class BEControlledClutch : BEClutch
{
    private static readonly AssetLocation SwitchSound =
        new("game:sounds/effect/woodswitch");

    public bool Toggle(IPlayer? byPlayer) => SetControlledEngaged(!Engaged, byPlayer);

    public bool SetControlledEngaged(bool value, IPlayer? byPlayer)
    {
        if (Engaged == value) return true;

        Engaged = value;
        MarkDirty(true);
        NotifyTransmission();
        if (Api?.Side == EnumAppSide.Server)
        {
            Api.World.PlaySoundAt(
                SwitchSound, Pos, 0, byPlayer,
                randomizePitch: false, range: 16, volume: 1);
        }
        return true;
    }

    public void HandleNeighbourChange(BlockPos neighbourPosition)
    {
        if (!neighbourPosition.Equals(Pos.AddCopy(Facing))) return;
        if (Api.World.BlockAccessor.GetBlock(neighbourPosition) is BlockControlledTransmission) return;

        Engaged = false;
        MarkDirty(true);
        Api.World.BlockAccessor.BreakBlock(Pos, null);
    }

    private void NotifyTransmission()
    {
        BlockPos transmissionPosition = Pos.AddCopy(Facing);
        Api?.World.BlockAccessor.GetBlockEntity(transmissionPosition)?
            .GetBehavior<BEBehaviorMPControlledTransmission>()?
            .SetControlledEngaged(Engaged);
    }
}
