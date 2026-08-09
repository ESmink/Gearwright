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
    private AssetLocation contentCode = new(HydraulicCodes.FreshWater);
    private double configuredPressure = HydraulicMath.FullSprinklerPressure;
    private GuiDialogCreativeFluidPump? dialog;

    public AssetLocation ConfiguredContentCode => contentCode;
    public double ConfiguredPressure => configuredPressure;
    public double ConfiguredTemperatureC => PipeContent.DefaultTemperatureC(contentCode);

    public override PumpOffer GetOffer()
    {
        if (!PipeContent.IsValid(Api.World, contentCode))
        {
            return new PumpOffer(this, contentCode, 0, "invalid-content");
        }
        return new PumpOffer(this, contentCode,
            Math.Clamp(configuredPressure, 0, HydraulicMath.MaximumCreativePressure), "running");
    }

    public void OpenConfigurationDialog()
    {
        if (Api is not ICoreClientAPI capi || dialog?.IsOpened() == true) return;
        dialog = new GuiDialogCreativeFluidPump(this, capi);
        dialog.TryOpen();
    }

    public void SendConfiguration(AssetLocation requestedContent, double requestedPressure)
    {
        if (Api is not ICoreClientAPI capi) return;
        CreativePumpPacket packet = new()
        {
            ContentCode = requestedContent.ToString(),
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
        try { code = new AssetLocation(packet.ContentCode); }
        catch { return; }
        if (!PipeContent.IsValid(Api.World, code)) return;

        contentCode = code;
        configuredPressure = Math.Clamp(packet.Pressure, 0, HydraulicMath.MaximumCreativePressure);
        MarkHydraulicsDirty();
    }

    protected override void ReadPumpState(ITreeAttribute state, IWorldAccessor world)
    {
        string storedCode = state.GetString("contentCode", HydraulicCodes.FreshWater);
        try { contentCode = new AssetLocation(storedCode); }
        catch { contentCode = new AssetLocation(HydraulicCodes.FreshWater); }
        configuredPressure = Math.Clamp(
            state.GetDouble("configuredPressure", HydraulicMath.FullSprinklerPressure),
            0, HydraulicMath.MaximumCreativePressure);
    }

    protected override void WritePumpState(ITreeAttribute state)
    {
        state.SetString("contentCode", contentCode.ToString());
        state.SetDouble("configuredPressure", configuredPressure);
    }

    [ProtoContract]
    public sealed class CreativePumpPacket
    {
        [ProtoMember(1)] public string ContentCode { get; set; } = HydraulicCodes.FreshWater;
        [ProtoMember(2)] public double Pressure { get; set; } = HydraulicMath.FullSprinklerPressure;
    }
}
