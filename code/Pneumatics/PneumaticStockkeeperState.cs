using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace Gearwright.Pneumatics;

internal sealed class PneumaticStockRow
{
    internal ItemStack? Sample;
    internal int Target, Stored, Reserved, Incoming;
    internal string Status = "inactive";
    internal int Needed => (int)Math.Max(0L, (long)Target - Stored - Reserved - Incoming);
}

/// <summary>A receipt sample is display metadata, never an inventory or recoverable cargo.</summary>
internal sealed class PneumaticPrintedOrder
{
    internal ItemStack Sample = null!;
    internal int Amount;
    private ITreeAttribute? original;

    internal static PneumaticPrintedOrder? Read(IAttribute? attribute, IWorldAccessor world)
    {
        if (attribute is not ITreeAttribute t || t["amount"] is not IntAttribute ||
            t.GetInt("amount") < 1 || t.GetInt("amount") > PneumaticStockkeeperState.MaximumTarget ||
            t["sample"] is not ItemstackAttribute) return null;
        try
        {
            var sample = t.GetItemstack("sample")?.Clone();
            if (sample == null || sample.StackSize != 1 || !sample.ResolveBlockOrItem(world) || !PneumaticInventory.Supported(sample)) return null;
            return new() { Sample = sample, Amount = t.GetInt("amount"), original = t.Clone() };
        }
        catch { return null; }
    }

    internal ITreeAttribute Write()
    {
        var t = original?.Clone() ?? new TreeAttribute();
        t.SetItemstack("sample", Sample.Clone()); t.SetInt("amount", Amount);
        return t;
    }
}

/// <summary>Independent schema; the existing transport and inventory documents are unchanged.</summary>
internal sealed class PneumaticStockkeeperState
{
    internal const string Key = "gearwrightPneumaticStockkeeper";
    internal const int Version = 2, MaximumTarget = 65535, HistoryLength = 10;
    internal const double PrintSeconds = 4, FeedDistance = 1.35, RollRadius = .8;
    internal const double GrainPeriod = 32;
    private IAttribute? original;
    internal bool Writable = true, Printing;
    internal int Revision, PrintedRows;
    internal string PrintId = "";
    internal double PrintProgress = 1, PaperAngle;
    internal double PaperPhase;
    internal PneumaticPrintedOrder? ActivePrint;
    internal PneumaticPrintedOrder[] History = Array.Empty<PneumaticPrintedOrder>();
    internal long NextConfiguration;
    internal PneumaticStockRow[] Rows = Enumerable.Range(0, 4).Select(_ => new PneumaticStockRow()).ToArray();

    internal static bool Matches(IWorldAccessor world, ItemStack? sample, ItemStack stack) =>
        sample != null && sample.Equals(world, stack, GlobalConstants.IgnoredStackAttributes);

    internal void BeginOrder(string parcel, ItemStack cargo)
    {
        var sample = cargo.Clone(); sample.StackSize = 1;
        ActivePrint = new() { Sample = sample, Amount = cargo.StackSize };
        PrintId = parcel; PrintProgress = 0; Printing = true;
    }

    internal void AdvancePrint(double seconds)
    {
        if (!Printing || !Writable) return;
        PrintProgress = Math.Min(1, PrintProgress + Math.Clamp(seconds, 0, .1) / PrintSeconds);
        if (PrintProgress < 1 - 1e-8) return;
        PrintProgress = 1; Printing = false; PrintedRows = Math.Min(4, PrintedRows + 1);
        PaperAngle = (PaperAngle + FeedDistance / RollRadius * 180 / Math.PI) % 360;
        PaperPhase = (PaperPhase + FeedDistance) % GrainPeriod;
        if (ActivePrint != null) History = new[] { ActivePrint }.Concat(History).Take(HistoryLength).ToArray();
        ActivePrint = null;
    }

    internal static PneumaticStockkeeperState Read(IAttribute? attribute, IWorldAccessor world)
    {
        var s = new PneumaticStockkeeperState { original = attribute?.Clone() };
        if (attribute == null) return s;
        s.Writable = false;
        if (attribute is not ITreeAttribute t || t["schemaVersion"] is not IntAttribute ||
            t.GetInt("schemaVersion") < 1 || t.GetInt("schemaVersion") > Version) return s;
        bool Int(ITreeAttribute tree, string key, int max) => !tree.HasAttribute(key) ||
            tree[key] is IntAttribute && tree.GetInt(key) >= 0 && tree.GetInt(key) <= max;
        bool Number(string key, double max) => !t.HasAttribute(key) || t[key] is DoubleAttribute &&
            double.IsFinite(t.GetDouble(key)) && t.GetDouble(key) >= 0 && t.GetDouble(key) <= max;
        if (!Int(t, "revision", int.MaxValue) || !Int(t, "printedRows", 4) || !Number("printProgress", 1) ||
            !Number("paperAngle", 360) || t.HasAttribute("printing") && t["printing"] is not BoolAttribute ||
            t.HasAttribute("printId") && t["printId"] is not StringAttribute) return s;
        s.Revision = t.GetInt("revision"); s.PrintedRows = t.GetInt("printedRows");
        s.PrintProgress = t.GetDouble("printProgress", 1); s.PaperAngle = t.GetDouble("paperAngle");
        s.Printing = t.GetBool("printing"); s.PrintId = t.GetString("printId", "");
        if (s.PrintId != "" && !Guid.TryParseExact(s.PrintId, "N", out _) || s.Printing && s.PrintId == "") return s;
        for (int i = 0; i < 4; i++)
        {
            if (!t.HasAttribute("row" + i)) continue;
            if (t["row" + i] is not ITreeAttribute row || !Int(row, "target", MaximumTarget) ||
                !Int(row, "stored", int.MaxValue) || !Int(row, "reserved", int.MaxValue) || !Int(row, "incoming", int.MaxValue) ||
                row.HasAttribute("status") && row["status"] is not StringAttribute) return s;
            var r = s.Rows[i]; r.Target = row.GetInt("target"); r.Stored = row.GetInt("stored");
            r.Reserved = row.GetInt("reserved"); r.Incoming = row.GetInt("incoming"); r.Status = row.GetString("status", "inactive");
            if (!row.HasAttribute("sample")) continue;
            if (row["sample"] is not ItemstackAttribute) return s;
            try
            {
                r.Sample = row.GetItemstack("sample")?.Clone();
                if (r.Sample == null || !r.Sample.ResolveBlockOrItem(world) || r.Sample.StackSize != 1 || !PneumaticInventory.Supported(r.Sample)) return s;
            }
            catch { return s; }
        }
        // Sequential 1 -> 2 migration: old printers never stored item identities.
        // Keep their targets and cycle, and leave the old paper blank. Only new
        // orders can create named receipts. The original tree remains untouched.
        if (t.GetInt("schemaVersion") == 1) { s.Writable = true; return s; }
        if (!Number("paperPhase", GrainPeriod) || !Int(t, "historyCount", HistoryLength)) return s;
        s.PaperPhase = t.GetDouble("paperPhase");
        if (t.HasAttribute("activePrint"))
        {
            s.ActivePrint = PneumaticPrintedOrder.Read(t["activePrint"], world);
            if (s.ActivePrint == null || !s.Printing) return s;
        }
        s.History = new PneumaticPrintedOrder[t.GetInt("historyCount")];
        for (int i = 0; i < s.History.Length; i++)
        {
            var entry = PneumaticPrintedOrder.Read(t["printHistory" + i], world);
            if (entry == null) return s;
            s.History[i] = entry;
        }
        s.Writable = true; return s;
    }

    internal IAttribute Write()
    {
        if (!Writable) return original!.Clone();
        var t = original is ITreeAttribute tree ? tree.Clone() : new TreeAttribute();
        t.SetInt("schemaVersion", Version); t.SetInt("revision", Revision); t.SetBool("printing", Printing);
        t.SetDouble("printProgress", PrintProgress); t.SetDouble("paperAngle", PaperAngle);
        t.SetInt("printedRows", PrintedRows); t.SetString("printId", PrintId);
        t.SetDouble("paperPhase", PaperPhase); t.SetInt("historyCount", History.Length);
        if (ActivePrint == null) t.RemoveAttribute("activePrint"); else t["activePrint"] = ActivePrint.Write();
        for (int i = 0; i < History.Length; i++) t["printHistory" + i] = History[i].Write();
        for (int i = 0; i < 4; i++)
        {
            var row = t.GetTreeAttribute("row" + i)?.Clone() ?? new TreeAttribute(); var r = Rows[i];
            row.SetInt("target", r.Target); row.SetInt("stored", r.Stored); row.SetInt("reserved", r.Reserved);
            row.SetInt("incoming", r.Incoming); row.SetString("status", r.Status);
            if (r.Sample == null) row.RemoveAttribute("sample"); else row.SetItemstack("sample", r.Sample.Clone());
            t["row" + i] = row;
        }
        return t;
    }
}
