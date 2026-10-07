using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

internal sealed class StockkeeperRowEdit
{
    public int Target;
    public string Source = "keep";
    public int Slot;
}
internal sealed class StockkeeperEdit
{
    public int Revision;
    public StockkeeperRowEdit[] Rows = Array.Empty<StockkeeperRowEdit>();
}

public sealed partial class BlockEntityPneumaticTransport
{
    private GuiDialogPneumaticStockkeeper? stockkeeperDialog;
    internal void OpenStockkeeperDialog()
    {
        if (Api is not ICoreClientAPI client || stockkeeperDialog?.IsOpened() == true) return;
        stockkeeperDialog?.Dispose(); stockkeeperDialog = new(this, client); stockkeeperDialog.TryOpen();
    }
    internal void SendStockkeeperConfiguration(StockkeeperEdit edit)
    {
        if (Api is ICoreClientAPI client) client.Network.SendBlockEntityPacket(Pos, 2103, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(edit)));
    }
    private void ReceiveStockkeeperConfiguration(IPlayer player, byte[] data)
    {
        if (Api.Side != EnumAppSide.Server || !IsSmartReceiver || !CanWrite || data.Length > 4096 ||
            !PneumaticPlacement.InReach(player, Pos) || !Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use) ||
            Stockkeeper.NextConfiguration > Api.World.ElapsedMilliseconds) return;
        Stockkeeper.NextConfiguration = Api.World.ElapsedMilliseconds + 300;
        StockkeeperEdit? edit;
        try { edit = JsonConvert.DeserializeObject<StockkeeperEdit>(Encoding.UTF8.GetString(data), new JsonSerializerSettings { MaxDepth = 8 }); }
        catch { return; }
        if (edit?.Rows == null || edit.Rows.Length != 4 || edit.Rows.Any(r => r == null)) return;
        var rows = new PneumaticStockRow[4];
        for (int i = 0; i < 4; i++)
        {
            var change = edit.Rows[i]; ItemStack? sample;
            if (change.Source == "keep") sample = Stockkeeper.Rows[i].Sample?.Clone();
            else if (change.Source == "clear") sample = null;
            else
            {
                if (change.Source is not ("hotbar" or "backpack")) return;
                var inventory = player.InventoryManager.GetOwnInventory(change.Source);
                if (inventory == null || change.Slot < 0 || change.Slot >= inventory.Count || inventory[change.Slot].Empty) return;
                sample = inventory[change.Slot].Itemstack!.Clone(); sample.StackSize = 1;
                if (!PneumaticInventory.Supported(sample)) return;
            }
            rows[i] = new() { Sample = sample, Target = change.Target };
        }
        Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().ConfigureStockkeeper(this, edit.Revision, rows);
    }
}
