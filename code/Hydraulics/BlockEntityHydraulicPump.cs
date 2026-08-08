using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public abstract class BlockEntityHydraulicPump : BlockEntityHydraulicNode
{
    private double lastSettledTotalHours = -1;
    private double fractionalItems;

    public double LastSettledTotalHours => lastSettledTotalHours;
    protected double FractionalItems
    {
        get => fractionalItems;
        set => fractionalItems = Math.Clamp(value, 0, 0.999999);
    }

    public override bool CanConnect(BlockFacing face) => !IsSourceFace(face);
    public abstract PumpOffer GetOffer();
    public abstract double ConsumeLitres(double requestedLitres);
    protected virtual bool IsSourceFace(BlockFacing face) => false;

    public void SetLastSettledTotalHours(double totalHours)
    {
        if (!CanWriteState) return;
        lastSettledTotalHours = totalHours;
        MarkHydraulicsDirty();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        PumpOffer offer = GetOffer();
        dsc.AppendLine(Lang.Get("gearwright:pump-pressure", offer.Pressure));
        if (offer.Pressure > 0) dsc.AppendLine(Lang.Get("gearwright:pipe-liquid", offer.LiquidCode));
        dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + offer.StatusCode));
        if (NetworkStatusCode != offer.StatusCode)
        {
            dsc.AppendLine(Lang.Get("gearwright:hydraulic-status-" + NetworkStatusCode));
        }
    }

    protected override void ReadKnownState(ITreeAttribute state, IWorldAccessor world)
    {
        lastSettledTotalHours = state.GetDouble("lastSettledTotalHours", -1);
        fractionalItems = Math.Clamp(state.GetDouble("fractionalItems", 0), 0, 0.999999);
        ReadPumpState(state, world);
    }

    protected override void WriteKnownState(ITreeAttribute state)
    {
        state.SetDouble("lastSettledTotalHours", lastSettledTotalHours);
        state.SetDouble("fractionalItems", fractionalItems);
        WritePumpState(state);
    }

    protected virtual void ReadPumpState(ITreeAttribute state, IWorldAccessor world) { }
    protected virtual void WritePumpState(ITreeAttribute state) { }
}
