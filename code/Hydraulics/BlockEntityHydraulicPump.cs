using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public abstract class BlockEntityHydraulicPump : BlockEntityHydraulicNode
{
    public override bool CanConnect(BlockFacing face) => !IsSourceFace(face);
    public abstract PumpOffer GetOffer();
    protected virtual bool IsSourceFace(BlockFacing face) => false;

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        PumpOffer offer = GetOffer();
        dsc.AppendLine(Lang.Get("gearwright:pump-pressure", offer.Pressure));
        if (offer.Pressure > 0) dsc.AppendLine(Lang.Get("gearwright:pipe-content", offer.ContentCode));
        dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + offer.StatusCode));
        if (NetworkStatusCode != offer.StatusCode)
        {
            dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + NetworkStatusCode));
        }
    }

    protected override void ReadKnownState(ITreeAttribute state, IWorldAccessor world)
    {
        ReadPumpState(state, world);
    }

    protected override void WriteKnownState(ITreeAttribute state)
    {
        WritePumpState(state);
    }

    protected virtual void ReadPumpState(ITreeAttribute state, IWorldAccessor world) { }
    protected virtual void WritePumpState(ITreeAttribute state) { }
}
