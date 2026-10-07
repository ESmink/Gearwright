using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

public sealed partial class PneumaticNetworkSystem
{
    internal bool ConfigureStockkeeper(BlockEntityPneumaticTransport host, int revision, PneumaticStockRow[] rows)
    {
        if (!host.IsSmartReceiver || !host.CanWrite || revision != host.Stockkeeper.Revision || revision == int.MaxValue || rows.Length != 4) return false;
        if (rows.Any(r => r.Target < 0 || r.Target > PneumaticStockkeeperState.MaximumTarget ||
            r.Sample != null && (r.Sample.StackSize != 1 || !PneumaticInventory.Supported(r.Sample)))) return false;
        for (int i = 0; i < rows.Length; i++)
            for (int j = i + 1; j < rows.Length; j++)
                if (rows[j].Sample != null && PneumaticStockkeeperState.Matches(api!.World, rows[i].Sample, rows[j].Sample!)) return false;
        bool ok = Transfer(new[] { host }, () => { host.Stockkeeper.Rows = rows; host.Stockkeeper.Revision++; });
        if (ok) RefreshStockkeeper(host);
        else SetStatus(host, "save-paused");
        return ok;
    }

    private void RefreshStockkeeper(BlockEntityPneumaticTransport receiver)
    {
        var target = receiver.Chest;
        bool access = target != null && InventoryAccess(receiver.State.Owner, target);
        // Every order owner is adjacent to its destination chest. A missing
        // neighbour chunk or frozen route leaves reservations uncertain; wait
        // rather than treating unloaded cargo as a new shortage.
        bool uncertain = target != null && BlockFacing.ALLFACES.Any(face =>
        {
            var pos = target.Chest.Pos.AddCopy(face);
            if (!Loaded(pos)) return true;
            if (api!.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityPneumaticTransport h) return false;
            return !h.CanWrite || h.State.Outstanding != "" &&
                (h.State.OrderRoute.Length == 0 || h.State.OrderRoute.Any(p => !Loaded(ParsePosition(p)!)));
        });
        foreach (var row in receiver.Stockkeeper.Rows)
        {
            row.Stored = row.Reserved = row.Incoming = 0;
            row.Status = row.Sample == null || row.Target == 0 ? "inactive" : "no-offer";
            if (row.Sample == null) continue;
            if (target == null) { row.Status = "no-inventory"; continue; }
            if (!access) { row.Status = "protected-route"; continue; }
            row.Stored = target.Inventory.Where(s => !s.Empty && PneumaticStockkeeperState.Matches(api!.World, row.Sample, s.Itemstack)).Sum(s => s.StackSize);
            foreach (var h in hosts.Values)
            {
                var cargo = h.State.Cargo;
                if (cargo == null || !PneumaticStockkeeperState.Matches(api!.World, row.Sample, cargo) ||
                    !(h.State.DeliveryInventory == target.Receipt.Instance || h.State.DeliveryInventory == "" && h.State.Destination == receiver.State.Instance)) continue;
                if (h.State.Loading) row.Reserved += cargo.StackSize; else row.Incoming += cargo.StackSize;
            }
            if (row.Target == 0) continue;
            row.Status = uncertain ? "waiting-route" : row.Needed == 0 ? (row.Reserved + row.Incoming > 0 ? "ordered" : "stocked") :
                receiver.Air <= 0 ? "no-air" : target.Capacity(api!.World, row.Sample) == 0 ? "destination-full" : "no-offer";
        }
        receiver.MarkDirty(false);
    }

    private int LimitStockOrder(BlockEntityPneumaticTransport receiver, ItemStack offered, int capacity)
    {
        if (!receiver.IsSmartReceiver) return capacity;
        // Order() refreshed these rows after every earlier receiver's atomic
        // reservation. Offer selection itself does not mutate inventories.
        var row = receiver.Stockkeeper.Rows.FirstOrDefault(r => r.Target > 0 && PneumaticStockkeeperState.Matches(api!.World, r.Sample, offered));
        return row == null || row.Status is "waiting-route" or "protected-route" or "no-inventory" ? 0 : Math.Min(capacity, row.Needed);
    }
}
