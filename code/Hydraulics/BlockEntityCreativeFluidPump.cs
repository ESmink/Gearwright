using System;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Gearwright.Hydraulics;

public sealed class BlockEntityCreativeFluidPump : BlockEntityHydraulicPump
{
    private const int ConfigurePacketId = 2101;
    private AssetLocation liquidCode = new(HydraulicCodes.FreshWater);
    private double configuredPressure = HydraulicMath.FullSprinklerPressure;
    private GuiDialogCreativeFluidPump? dialog;

    public AssetLocation ConfiguredLiquidCode => liquidCode;
    public double ConfiguredPressure => configuredPressure;

    public override PumpOffer GetOffer()
    {
        Item? item = Api.World.GetItem(liquidCode);
        if (item?.Attributes?["waterTightContainerProps"].Exists != true)
        {
            return new PumpOffer(this, liquidCode, 0, "invalid-liquid");
        }
        return new PumpOffer(this, liquidCode, Math.Clamp(configuredPressure, 0, HydraulicMath.MaximumCreativePressure), "running");
    }

    public override double ConsumeLitres(double requestedLitres) => Math.Max(0, requestedLitres);

    public void OpenConfigurationDialog()
    {
        if (Api is not ICoreClientAPI capi || dialog?.IsOpened() == true) return;
        dialog = new GuiDialogCreativeFluidPump(this, capi);
        dialog.TryOpen();
    }

    public void SendConfiguration(AssetLocation requestedLiquid, double requestedPressure)
    {
        if (Api is not ICoreClientAPI capi) return;
        CreativePumpPacket packet = new()
        {
            LiquidCode = requestedLiquid.ToString(),
            Pressure = requestedPressure
        };
        capi.Network.SendBlockEntityPacket(Pos, ConfigurePacketId, SerializerUtil.Serialize(packet));
    }

    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
        if (packetid != ConfigurePacketId || !CanWriteState ||
            !Api.World.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.Use)) return;

        CreativePumpPacket packet;
        try { packet = SerializerUtil.Deserialize<CreativePumpPacket>(data); }
        catch { return; }
        AssetLocation code;
        try { code = new AssetLocation(packet.LiquidCode); }
        catch { return; }
        Item? item = Api.World.GetItem(code);
        if (item?.Attributes?["waterTightContainerProps"].Exists != true) return;

        liquidCode = code;
        configuredPressure = Math.Clamp(packet.Pressure, 0, HydraulicMath.MaximumCreativePressure);
        MarkHydraulicsDirty();
    }

    protected override void ReadPumpState(ITreeAttribute state, IWorldAccessor world)
    {
        string storedCode = state.GetString("liquidCode", HydraulicCodes.FreshWater);
        try { liquidCode = new AssetLocation(storedCode); }
        catch { liquidCode = new AssetLocation(HydraulicCodes.FreshWater); }
        configuredPressure = Math.Clamp(
            state.GetDouble("configuredPressure", HydraulicMath.FullSprinklerPressure),
            0, HydraulicMath.MaximumCreativePressure);
    }

    protected override void WritePumpState(ITreeAttribute state)
    {
        state.SetString("liquidCode", liquidCode.ToString());
        state.SetDouble("configuredPressure", configuredPressure);
    }

    [ProtoContract]
    public sealed class CreativePumpPacket
    {
        [ProtoMember(1)] public string LiquidCode { get; set; } = HydraulicCodes.FreshWater;
        [ProtoMember(2)] public double Pressure { get; set; } = HydraulicMath.FullSprinklerPressure;
    }
}
