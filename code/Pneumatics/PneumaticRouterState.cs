using System;
using System.Linq;
using Gearwright.Hydraulics;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>New independent schema; existing host documents remain schema 3.</summary>
internal sealed class PneumaticRouterState
{
    internal const string Key = "gearwrightPneumaticRouter";
    private IAttribute? original;
    internal bool Writable = true;
    internal PneumaticRouterConfiguration Configuration = new();
    internal BlockFacing Mount = BlockFacing.DOWN, Forward = BlockFacing.EAST;
    internal int Cursor, TransitInput = 1, TransitOutput = 3, SupplyPort;
    internal int ReadyInput = 1, ReadyOutput = 3;
    internal double Preparation;
    internal double RestAngle;
    internal string ExpectedParcel = "";
    internal long NextConfiguration;
    internal long NextRouteCheck;
    internal bool Lost;
    internal const double TransitSeconds = 1, PrepareSeconds = .4;

    internal float[] Matrix => PumpOrientation.Matrix(Forward, Mount.Opposite);
    internal BlockFacing Face(int port)
    {
        var local = new[] { BlockFacing.WEST, BlockFacing.SOUTH, BlockFacing.EAST, BlockFacing.NORTH }[port - 1].Normali;
        var m = Matrix;
        int x = (int)Math.Round(m[0] * local.X + m[4] * local.Y + m[8] * local.Z);
        int y = (int)Math.Round(m[1] * local.X + m[5] * local.Y + m[9] * local.Z);
        int z = (int)Math.Round(m[2] * local.X + m[6] * local.Y + m[10] * local.Z);
        return BlockFacing.ALLFACES.First(f => f.Normali.X == x && f.Normali.Y == y && f.Normali.Z == z);
    }
    internal int Port(BlockFacing face) => Enumerable.Range(1, 4).FirstOrDefault(n => Face(n) == face);
    internal bool Allows(ItemStack stack, int input, int output, int[] outputs, bool committed)
    {
        // Committed cargo keeps its receiver and route. Priority and turn
        // selection belong to destination-aware path finding, not handoffs.
        return input >= 1 && input <= 4 && output >= 1 && output <= 4 && outputs.Contains(output) &&
            PneumaticRouterFilters.Match(Configuration.Ports[output - 1], stack);
    }
    internal int Choose(int[] choices) => choices.OrderBy(n => (n - 1 - Cursor + 4) % 4).FirstOrDefault();

    internal static PneumaticRouterState Read(IAttribute? attribute)
    {
        var state = new PneumaticRouterState { original = attribute?.Clone() };
        if (attribute == null) return state;
        state.Writable = false;
        if (attribute is not ITreeAttribute t || t["schemaVersion"] is not IntAttribute || t.GetInt("schemaVersion") < 1 || t.GetInt("schemaVersion") > 2) return state;
        bool Text(string key) => !t.HasAttribute(key) || t[key] is StringAttribute;
        bool Integer(string key, int low, int high) => !t.HasAttribute(key) || t[key] is IntAttribute && t.GetInt(key) >= low && t.GetInt(key) <= high;
        if (!Text("mount") || !Text("forward") || !Text("configuration") || !Text("expectedParcel") ||
            !Integer("cursor", 0, 3) || !Integer("transitInput", 1, 4) || !Integer("transitOutput", 1, 4) ||
            !Integer("readyInput", 1, 4) || !Integer("readyOutput", 1, 4) || !Integer("supplyPort", 0, 4) ||
            t.HasAttribute("lost") && t["lost"] is not BoolAttribute ||
            t.HasAttribute("preparation") && (t["preparation"] is not DoubleAttribute ||
                !double.IsFinite(t.GetDouble("preparation")) || t.GetDouble("preparation") < 0 || t.GetDouble("preparation") > 1)) return state;
        var mount = PneumaticState.Face(t.GetString("mount", "down"));
        var forward = PneumaticState.Face(t.GetString("forward", "east"));
        if (mount == null || forward == null || mount.Axis == forward.Axis) return state;
        var configuration = PneumaticRouterConfiguration.Parse(t.GetString("configuration", "{}"));
        if (configuration == null) return state;
        state.Configuration = configuration;
        state.Mount = mount; state.Forward = forward; state.Cursor = t.GetInt("cursor");
        state.TransitInput = t.GetInt("transitInput", 1); state.TransitOutput = t.GetInt("transitOutput", 3);
        state.ReadyInput = t.GetInt("readyInput", 1); state.ReadyOutput = t.GetInt("readyOutput", 3);
        state.SupplyPort = t.GetInt("supplyPort"); state.Preparation = t.GetDouble("preparation");
        if (t.HasAttribute("restAngle") && (t["restAngle"] is not DoubleAttribute || !double.IsFinite(t.GetDouble("restAngle")))) return state;
        state.RestAngle = t.GetDouble("restAngle");
        state.ExpectedParcel = t.GetString("expectedParcel", "");
        // Schema 1 -> 2 adds a route alert; old parcels retain their full state.
        state.Lost = t.GetBool("lost");
        if (state.ExpectedParcel != "" && !Guid.TryParseExact(state.ExpectedParcel, "N", out _)) return state;
        state.Writable = true; return state;
    }

    internal IAttribute Write()
    {
        if (!Writable) return original!.Clone();
        var t = original is ITreeAttribute tree ? tree.Clone() : new TreeAttribute();
        t.SetInt("schemaVersion", 2); t.SetString("mount", Mount.Code); t.SetString("forward", Forward.Code);
        // Merge recognised configuration fields into the original JSON rather
        // than discarding unknown fields from this schema's tolerant reader.
        var old = t.GetString("configuration", "{}");
        var json = Newtonsoft.Json.Linq.JObject.Parse(old);
        var current = Newtonsoft.Json.Linq.JObject.FromObject(Configuration);
        json.Merge(current, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Merge });
        // Arrays represent explicit editable lists; preserve unknown keys on
        // retained ports/rules, but honour removals instead of retaining ghosts.
        if (json["Ports"] is Newtonsoft.Json.Linq.JArray ports)
        {
            while (ports.Count > 4) ports.RemoveAt(ports.Count - 1);
            for (int i = 0; i < 4; i++) if (ports[i]?["Rules"] is Newtonsoft.Json.Linq.JArray rules)
                while (rules.Count > Configuration.Ports[i].Rules.Length) rules.RemoveAt(rules.Count - 1);
        }
        t.SetString("configuration", json.ToString(Newtonsoft.Json.Formatting.None));
        t.SetInt("cursor", Cursor); t.SetInt("transitInput", TransitInput); t.SetInt("transitOutput", TransitOutput);
        t.SetInt("readyInput", ReadyInput); t.SetInt("readyOutput", ReadyOutput); t.SetInt("supplyPort", SupplyPort);
        t.SetDouble("preparation", Preparation); t.SetString("expectedParcel", ExpectedParcel);
        t.SetDouble("restAngle", RestAngle);
        t.SetBool("lost", Lost);
        return t;
    }
}
