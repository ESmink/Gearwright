using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>One independent, versioned host document. Unknown attributes survive writes.</summary>
internal sealed class PneumaticState
{
    internal const string Key = "gearwrightPneumaticTransport";
    internal const int Version = 3;
    private IAttribute? original;
    public bool Writable { get; private set; } = true;
    public string Instance = Guid.NewGuid().ToString("N");
    public string Owner = "";
    public BlockFacing Input = BlockFacing.WEST, Output = BlockFacing.EAST;
    public BlockFacing InventoryFace = BlockFacing.DOWN;
    public string Parcel = "", Destination = "", Receipt = "";
    public bool DeliveryOutlet;
    public BlockFacing? DeliveryFace;
    public string DeliveryInventory = "";
    public ItemStack? Cargo;
    public double Progress;
    public double ReturnProgress;
    public bool Returning;
    public bool Loading;
    public string Outstanding = "";
    public string[] OrderRoute = Array.Empty<string>();
    public string[] Route = Array.Empty<string>();
    public int RouteIndex;
    internal bool AtDeliveryOutlet => DeliveryOutlet && (Destination == Instance || Route.Length == 0 || RouteIndex == Route.Length - 1);
    internal double HandoffProgress => Loading ? (AtDeliveryOutlet ? 240.0 : 224.0) / 360 : 1;

    internal static BlockFacing? Face(string? code) => Array.Find(BlockFacing.ALLFACES, f => f.Code == code);

    public static PneumaticState Read(IAttribute? value, IWorldAccessor world)
    {
        var s = new PneumaticState { original = value?.Clone() };
        if (value == null) return s;
        s.Writable = false;
        if (value is not ITreeAttribute t || t["schemaVersion"] is not IntAttribute || t.GetInt("schemaVersion") < 1 || t.GetInt("schemaVersion") > Version)
            return s;
        int version = t.GetInt("schemaVersion");
        bool Text(string key) => !t.HasAttribute(key) || t[key] is StringAttribute;
        bool Number(string key) => !t.HasAttribute(key) || t[key] is DoubleAttribute &&
            double.IsFinite(t.GetDouble(key)) && t.GetDouble(key) >= 0 && t.GetDouble(key) <= 1;
        if (!Text("instance") || !Text("owner") || !Text("parcel") || !Text("destination") || !Text("receipt") ||
            !Text("input") || !Text("output") || version >= 2 && !Text("inventoryFace") || !Number("progress") || !Number("returnProgress") ||
            t.HasAttribute("returning") && t["returning"] is not BoolAttribute ||
            t.HasAttribute("loading") && t["loading"] is not BoolAttribute || !Text("outstanding") ||
            t.HasAttribute("routeIndex") && t["routeIndex"] is not IntAttribute ||
            t.HasAttribute("route") && t["route"] is not StringArrayAttribute ||
            t.HasAttribute("orderRoute") && t["orderRoute"] is not StringArrayAttribute) return s;
        var input = Face(t.GetString("input", "west"));
        var output = Face(t.GetString("output", "east"));
        if (input == null || output == null || input == output || !Guid.TryParseExact(t.GetString("instance"), "N", out _)) return s;
        if (t.HasAttribute("cargo"))
        {
            if (t["cargo"] is not ItemstackAttribute) return s;
            try
            {
                s.Cargo = t.GetItemstack("cargo")?.Clone();
                if (s.Cargo == null || !s.Cargo.ResolveBlockOrItem(world) || s.Cargo.StackSize <= 0 ||
                    s.Cargo.StackSize > 8 || !Guid.TryParseExact(t.GetString("parcel"), "N", out _)) return s;
            }
            catch { return s; }
        }
        else if (t.GetString("parcel", "") != "") return s;
        s.Instance = t.GetString("instance"); s.Owner = t.GetString("owner", "");
        s.Input = input; s.Output = output;
        // Schema 1 machines had a fixed downward inventory branch. Preserve
        // their placement and cargo while adding the independently chosen face.
        var inventoryFace = version == 1 ? BlockFacing.DOWN : Face(t.GetString("inventoryFace", "down"));
        if (inventoryFace == null) return s;
        s.InventoryFace = inventoryFace;
        // Schema 2 -> 3: old parcels still target the receiver's branch. New
        // orders freeze the selected face and inventory identity independently.
        if (version >= 3)
        {
            if (!Text("deliveryFace") || !Text("deliveryInventory") ||
                t.HasAttribute("deliveryOutlet") && t["deliveryOutlet"] is not BoolAttribute) return s;
            s.DeliveryOutlet = t.GetBool("deliveryOutlet");
            string face = t.GetString("deliveryFace", "");
            s.DeliveryFace = face == "" ? null : Face(face);
            s.DeliveryInventory = t.GetString("deliveryInventory", "");
            if (face != "" && s.DeliveryFace == null || s.DeliveryOutlet && s.DeliveryFace == null ||
                s.DeliveryInventory != "" && !Guid.TryParseExact(s.DeliveryInventory, "N", out _)) return s;
        }
        s.Parcel = t.GetString("parcel", ""); s.Destination = t.GetString("destination", "");
        s.Receipt = t.GetString("receipt", "");
        s.Progress = t.GetDouble("progress"); s.ReturnProgress = t.GetDouble("returnProgress");
        s.Returning = t.GetBool("returning"); s.Writable = true;
        s.Loading = t.GetBool("loading"); s.Outstanding = t.GetString("outstanding", "");
        s.Route = (t["route"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
        s.OrderRoute = (t["orderRoute"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
        s.RouteIndex = t.GetInt("routeIndex");
        int routeLimit = version == 1 ? 256 : PneumaticAir.MaximumNodes;
        if (s.Route.Length > routeLimit || s.OrderRoute.Length > routeLimit || s.RouteIndex < 0 ||
            s.Route.Length > 0 && s.RouteIndex >= s.Route.Length ||
            Array.Exists(s.Route, p => PneumaticNetworkSystem.ParsePosition(p) == null) ||
            Array.Exists(s.OrderRoute, p => PneumaticNetworkSystem.ParsePosition(p) == null)) s.Writable = false;
        return s;
    }

    public IAttribute Write()
    {
        if (!Writable) return original!.Clone();
        var t = original is ITreeAttribute tree ? tree.Clone() : new TreeAttribute();
        t.SetInt("schemaVersion", Version); t.SetString("instance", Instance); t.SetString("owner", Owner);
        t.SetString("input", Input.Code); t.SetString("output", Output.Code);
        t.SetString("inventoryFace", InventoryFace.Code);
        t.SetString("parcel", Parcel); t.SetString("destination", Destination); t.SetString("receipt", Receipt);
        t.SetBool("deliveryOutlet", DeliveryOutlet); t.SetString("deliveryFace", DeliveryFace?.Code ?? "");
        t.SetString("deliveryInventory", DeliveryInventory);
        t.SetDouble("progress", Progress); t.SetDouble("returnProgress", ReturnProgress); t.SetBool("returning", Returning);
        t.SetBool("loading", Loading); t.SetString("outstanding", Outstanding);
        t["route"] = new StringArrayAttribute(Route); t["orderRoute"] = new StringArrayAttribute(OrderRoute); t.SetInt("routeIndex", RouteIndex);
        if (Cargo == null) t.RemoveAttribute("cargo"); else t.SetItemstack("cargo", Cargo.Clone());
        return t;
    }

    public void ClearCargo() { Cargo = null; Parcel = ""; Destination = ""; DeliveryOutlet = false; DeliveryFace = null;
        DeliveryInventory = ""; Progress = 0; Loading = false; Route = Array.Empty<string>(); RouteIndex = 0; }
}
