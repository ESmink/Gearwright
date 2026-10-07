using System;
using System.IO;
using System.Linq;
using Gearwright.Pneumatics;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static Gearwright.Contracts.PumpRuntimeFixture;
using Rig = Gearwright.Contracts.PneumaticTransportFixture.Rig;

namespace Gearwright.Contracts;

internal static class PneumaticStockkeeperFixture
{
    private static void Smart(Rig r, int target)
    {
        Set(r.Receiver, "Block", new Block { Code = new("gearwright:pneumatic-smart-receiver") });
        var sample = r.Source.Inventory[0].Itemstack!.Clone(); sample.StackSize = 1;
        r.Receiver.Stockkeeper.Rows[0] = new() { Sample = sample, Target = target };
    }
    private static int Delivered(Rig r) => r.Target.Inventory.Sum(s => s.StackSize);
    private static byte[] Bytes(IAttribute value) { using var s = new MemoryStream(); using var w = new BinaryWriter(s); value.ToBytes(w); return s.ToArray(); }
    internal static void Run(Action<bool, string> check)
    {
        foreach (bool router in new[] { false, true })
        {
            using var r = new Rig(); r.Fill(); Smart(r, 17);
            if (router)
            {
                var old = r.Hosts.Single(h => h.Pos.X == 2); r.System.Unregister(old); r.Hosts.Remove(old); r.Entities.Remove(old.Pos); r.Host(2, 2, "router");
            }
            int orders = 0; string last = ""; bool conserved = true, deficit = true;
            for (int tick = 0; tick < 1100; tick++)
            {
                r.Step(); conserved &= r.Total == 64;
                var s = r.Receiver.Stockkeeper;
                if (s.PrintId != "" && s.PrintId != last) { orders++; last = s.PrintId; }
                deficit &= Delivered(r) + r.Hosts.Sum(h => h.State.Cargo?.StackSize ?? 0) <= 17;
                if (tick % 11 == 0) r.Reload();
            }
            check(conserved && deficit && Delivered(r) == 17 && orders == 3 && r.Receiver.Stockkeeper.PrintedRows == 3,
                "Smart Receiver " + (router ? "router" : "direct") + " path fills an exact 17-item target in three printed orders across reloads");
            check(r.Receiver.Stockkeeper.History.Select(p => p.Amount).SequenceEqual(new[] { 1, 8, 8 }) &&
                r.Receiver.Stockkeeper.History.All(p => p.Sample.StackSize == 1 && p.Sample.Attributes.GetString("provenance") == "original stack"),
                "Printed receipts preserve each actual batch quantity and item attributes across direct/routed reloads");
            r.Target.Inventory[0].TakeOut(5);
            for (int tick = 0; tick < 300; tick++) r.Step();
            check(Delivered(r) == 17 && r.Total == 59 && r.Receiver.Stockkeeper.PrintId != last,
                "Removing five items requests exactly five replacements without consuming the ghost sample");
            var reduced = r.Receiver.Stockkeeper.Rows.Select(x => new PneumaticStockRow { Sample = x.Sample?.Clone(), Target = x.Target }).ToArray(); reduced[0].Target = 3;
            check(r.System.ConfigureStockkeeper(r.Receiver, 0, reduced), "Server accepts a valid current stock configuration");
            for (int tick = 0; tick < 80; tick++) r.Step();
            check(Delivered(r) == 17, "Lowering the stock target leaves existing surplus in the chest");
            check(!r.System.ConfigureStockkeeper(r.Receiver, 0, reduced), "Stale stock configurations cannot overwrite a newer revision");
            reduced[1].Sample = reduced[0].Sample?.Clone(); reduced[1].Target = 10;
            check(!r.System.ConfigureStockkeeper(r.Receiver, 1, reduced), "Duplicate samples cannot reserve the same shortage twice");
        }
        using (var r = new Rig())
        {
            r.Fill(); Smart(r, 1); r.Receiver.Stockkeeper.Rows[0].Sample!.Attributes.SetString("provenance", "different variant");
            for (int tick = 0; tick < 100; tick++) r.Step();
            check(Delivered(r) == 0 && r.Source.Inventory[0].StackSize == 64 && r.Receiver.Stockkeeper.PrintId == "",
                "A nonmatching sample neither extracts cargo nor starts a print cycle");
        }
        using (var r = new Rig())
        {
            r.Fill(); Smart(r, 16); r.Denied.Add(r.Target.Pos);
            for (int tick = 0; tick < 100; tick++) r.Step();
            check(r.Total == 64 && r.Source.Inventory[0].StackSize == 64, "Smart orders respect the destination's current claims");
            r.Denied.Clear();
            for (int tick = 0; tick < 100 && !r.Receiver.Stockkeeper.Printing; tick++) r.Step();
            double progress = r.Receiver.Stockkeeper.PrintProgress; string id = r.Receiver.Stockkeeper.PrintId;
            r.Unloaded.Add(r.Receiver.Pos);
            for (int tick = 0; tick < 20; tick++) r.Step();
            check(r.Receiver.Stockkeeper.PrintProgress == progress && r.Receiver.Stockkeeper.PrintId == id,
                "An unloaded Smart Receiver freezes the current print rather than starting another order");
        }
        using (var r = new Rig())
        {
            r.Fill(); Smart(r, 8); r.Store.Fail = true;
            for (int tick = 0; tick < 10; tick++) r.Step();
            check(r.Source.Inventory[0].StackSize == 64 && !r.Receiver.Stockkeeper.Printing && r.Receiver.Stockkeeper.PrintId == "" &&
                r.Receiver.Stockkeeper.ActivePrint == null && r.Receiver.Stockkeeper.History.Length == 0,
                "A failed atomic reservation rolls back the print and leaves all eight items at the sender");
        }
        using (var r = new Rig())
        {
            r.Fill(); Smart(r, 0);
            for (int i = 0; i < 4; i++)
            {
                var stack = new ItemStack(r.Item, 64); stack.Attributes.SetInt("variant", i);
                r.Source.Inventory[i].Itemstack = stack;
                var sample = stack.Clone(); sample.StackSize = 1;
                r.Receiver.Stockkeeper.Rows[i] = new() { Sample = sample, Target = i + 1 };
            }
            for (int tick = 0; tick < 800; tick++) { r.Step(); if (tick % 17 == 0) r.Reload(); }
            check(r.Total == 256 && Enumerable.Range(0, 4).All(i => r.Target.Inventory.Where(s => !s.Empty &&
                s.Itemstack!.Attributes.GetInt("variant") == i).Sum(s => s.StackSize) == i + 1),
                "All four sample variants reach their separate targets without crossing attributes");
        }
        using (var r = new Rig())
        {
            r.Fill(); Smart(r, 17);
            r.Host(5, 2, "tube", BlockFacing.WEST, BlockFacing.DOWN);
            var second = r.Host(5, 1, "smart-receiver", BlockFacing.UP, BlockFacing.DOWN); second.State.InventoryFace = BlockFacing.WEST;
            second.Stockkeeper.Rows[0] = new() { Sample = r.Receiver.Stockkeeper.Rows[0].Sample!.Clone(), Target = 17 };
            bool bounded = true;
            for (int tick = 0; tick < 900; tick++) { r.Step(); bounded &= Delivered(r) + r.Hosts.Sum(h => h.State.Cargo?.StackSize ?? 0) <= 17; }
            check(bounded && r.Total == 64 && Delivered(r) == 17,
                "Two Smart Receivers on one chest count each other's atomic reservations and do not over-order");
        }
        Persistence(check); Motion(check); PneumaticPrinterPaperFixture.Run(check);
    }
    private static void Persistence(Action<bool, string> check)
    {
        using var r = new Rig();
        var json = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/pneumatic-stockkeeper-schema1.json")));
        var tree = new TreeAttribute(); tree.SetInt("schemaVersion", (int)json["schemaVersion"]!);
        tree.SetInt("revision", (int)json["revision"]!); tree.SetBool("printing", (bool)json["printing"]!);
        tree.SetDouble("printProgress", (double)json["printProgress"]!); tree.SetDouble("paperAngle", (double)json["paperAngle"]!);
        tree.SetInt("printedRows", (int)json["printedRows"]!); tree.SetString("printId", (string)json["printId"]!);
        tree.SetString("futureSetting", (string)json["futureSetting"]!);
        var row = new TreeAttribute(); row.SetInt("target", (int)json["row0"]!["target"]!); row.SetString("futureRowSetting", (string)json["row0"]!["futureRowSetting"]!); tree["row0"] = row;
        var state = PneumaticStockkeeperState.Read(tree, r.World);
        var saved = (ITreeAttribute)state.Write(); var reloaded = PneumaticStockkeeperState.Read(saved, r.World);
        check(reloaded.Writable && saved.GetInt("schemaVersion") == 2 && reloaded.Rows[0].Target == 64 &&
            reloaded.History.Length == 0 && reloaded.ActivePrint == null && saved.GetString("futureSetting") == "preserve" &&
            saved.GetTreeAttribute("row0").GetString("futureRowSetting") == "preserve", "Schema-one Smart Receiver migrates to two without inventing old receipts or losing unknown fields");
        var legacyPrinting = tree.Clone(); legacyPrinting.SetBool("printing", true); legacyPrinting.SetDouble("printProgress", .75);
        legacyPrinting.SetString("printId", Guid.NewGuid().ToString("N")); legacyPrinting.SetDouble("paperAngle", 100);
        var migrated = PneumaticStockkeeperState.Read(legacyPrinting, r.World);
        migrated = PneumaticStockkeeperState.Read(migrated.Write(), r.World);
        for (int tick = 0; tick < 10; tick++) migrated.AdvancePrint(.1);
        check(migrated.Writable && !migrated.Printing && migrated.PaperAngle > 100 && migrated.History.Length == 0,
            "Schema-one in-progress printer finishes its existing feed without fabricating an item name");
        foreach (string failure in new[] { "future", "nan", "target", "sample", "type" })
        {
            var bad = saved.Clone();
            if (failure == "future") bad.SetInt("schemaVersion", 3);
            if (failure == "nan") bad.SetDouble("printProgress", double.NaN);
            if (failure == "target") bad.GetTreeAttribute("row0").SetInt("target", -1);
            if (failure == "sample") bad.GetTreeAttribute("row0").SetString("sample", "not a stack");
            if (failure == "type") bad.SetString("printing", "true");
            var protectedState = PneumaticStockkeeperState.Read(bad, r.World);
            check(!protectedState.Writable && Bytes(protectedState.Write()).SequenceEqual(Bytes(bad)), "Smart Receiver protects original bytes for " + failure);
        }
        check(PneumaticState.Version == 3 && PneumaticStockkeeperState.Read(null, r.World).Rows.Length == 4,
            "Smart state defaults are additive; existing pneumatic host schema remains three");
        ReceiptPersistence(r, check);
    }

    private static void ReceiptPersistence(Rig r, Action<bool, string> check)
    {
        var json = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures/pneumatic-stockkeeper-schema2.json")));
        var tree = new TreeAttribute();
        foreach (string field in new[] { "schemaVersion", "revision", "printedRows", "historyCount" }) tree.SetInt(field, (int)json[field]!);
        foreach (string field in new[] { "printProgress", "paperAngle", "paperPhase" }) tree.SetDouble(field, (double)json[field]!);
        tree.SetBool("printing", (bool)json["printing"]!); tree.SetString("printId", (string)json["printId"]!);
        tree.SetString("futureSetting", (string)json["futureSetting"]!);
        foreach (string field in new[] { "activePrint", "printHistory0", "printHistory1" })
        {
            var j = json[field]!; var entry = new TreeAttribute();
            entry.SetInt("amount", (int)j["amount"]!); entry.SetString("futureInk", (string)j["futureInk"]!);
            var sample = new ItemStack(r.Item, 1); sample.Attributes.SetInt("variant", (int)j["sampleVariant"]!);
            entry.SetItemstack("sample", sample); tree[field] = entry;
        }
        var state = PneumaticStockkeeperState.Read(tree, r.World);
        state = PneumaticStockkeeperState.Read(state.Write(), r.World);
        check(state.Writable && state.ActivePrint?.Amount == 3 && state.ActivePrint.Sample.Attributes.GetInt("variant") == 7 &&
            state.History.Select(h => h.Amount).SequenceEqual(new[] { 8, 6 }) && state.PaperPhase == 2.7,
            "Schema-two fixture retains current and previous item receipts, quantities and grain phase");
        for (int i = 0; i < 12; i++) state.AdvancePrint(.1);
        var saved = (ITreeAttribute)state.Write(); state = PneumaticStockkeeperState.Read(saved, r.World);
        check(state.Writable && state.History.Select(h => h.Amount).SequenceEqual(new[] { 3, 8, 6 }) && state.ActivePrint == null &&
            saved.GetTreeAttribute("printHistory0").GetString("futureInk") == "preserve-active" &&
            saved.GetTreeAttribute("printHistory1").GetString("futureInk") == "preserve-newest" &&
            saved.GetTreeAttribute("printHistory2").GetString("futureInk") == "preserve-oldest",
            "Completing and reloading a receipt moves its unknown metadata with the correct item");
        foreach (string failure in new[] { "amount", "sample", "missing", "count", "phase", "active" })
        {
            var bad = tree.Clone();
            if (failure == "amount") bad.GetTreeAttribute("activePrint").SetInt("amount", 0);
            if (failure == "sample") bad.GetTreeAttribute("printHistory0").SetString("sample", "invalid");
            if (failure == "missing") bad.RemoveAttribute("printHistory1");
            if (failure == "count") bad.SetInt("historyCount", 11);
            if (failure == "phase") bad.SetDouble("paperPhase", double.NaN);
            if (failure == "active") bad.SetBool("printing", false);
            var protectedState = PneumaticStockkeeperState.Read(bad, r.World);
            check(!protectedState.Writable && Bytes(protectedState.Write()).SequenceEqual(Bytes(bad)), "Receipt schema protects original bytes for " + failure);
        }
        for (int n = 0; n < 40; n++)
        {
            state.BeginOrder(Guid.NewGuid().ToString("N"), new ItemStack(r.Item, n + 1));
            for (int tick = 0; tick < 40; tick++) state.AdvancePrint(.1);
            state = PneumaticStockkeeperState.Read(state.Write(), r.World);
        }
        check(state.History.Length == PneumaticStockkeeperState.HistoryLength && state.History[0].Amount == 40 &&
            state.History[^1].Amount == 31 && state.PaperPhase >= 0 && state.PaperPhase < 32,
            "Receipt history and grain phase remain bounded through many orders and reloads");
    }
    private static void Motion(Action<bool, string> check)
    {
        using var r = new Rig();
        var s = new PneumaticStockkeeperState(); s.BeginOrder(Guid.NewGuid().ToString("N"), new ItemStack(r.Item, 8));
        bool ordered = true;
        for (int frame = 0; frame <= 120; frame++)
        { var p = PneumaticPrinterMotion.Pose(frame / 120d); ordered &= p.Press == 0 || p.Feed == 0; }
        check(ordered && PneumaticPrinterMotion.Pose(1).Travel == 0 && PneumaticPrinterMotion.Pose(1).Feed == 1.35,
            "Printer completes stamping before advancing paper, then returns to its parked position");
        check(PneumaticPrinterPaper.PrintX + PneumaticPrinterPaper.Feed(s, 1) - PneumaticPrinterPaper.LabelHeight / 2 > 6.09,
            "The complete named receipt emerges beyond the housing onto the visible strip");
        for (int tick = 0; tick < 40; tick++) s.AdvancePrint(.1);
        double angle = s.PaperAngle;
        check(!s.Printing && s.PrintedRows == 1 && angle > 90, "Finishing a print persists the advanced reel angle and visible record count");
        s.BeginOrder(Guid.NewGuid().ToString("N"), new ItemStack(r.Item, 3));
        check(PneumaticPrinterMotion.Bone("stock-takeup-roll", s, 0)!.RotationZ == -angle,
            "Starting the next order keeps the reel's previous rotation instead of snapping backward");
    }
}
