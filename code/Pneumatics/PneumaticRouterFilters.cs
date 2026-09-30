using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

internal sealed class PneumaticFilterRule
{
    public string Kind = "identity";
    public string Value = "";
    // Value remains a literal compatibility field. This field opts into logic.
    public string ValueExpression = "";
    public bool Exclude;
    private sealed record Parsed(string Text, PneumaticFilterExpression.Node? Node);
    private Parsed? parsed;
    internal PneumaticFilterExpression.Node? Expression
    {
        get
        {
            var text = ValueExpression;
            var current = parsed;
            if (current == null || current.Text != text) parsed = current = new(text, PneumaticFilterExpression.Parse(text));
            return current.Node;
        }
    }
    internal bool ValidExpression => ValueExpression != null && (ValueExpression == "" || Expression != null);
}

internal sealed class PneumaticPortSettings
{
    public string Role = "auto";
    public int Priority = 0;
    // Legacy Priority and Role remain in saves; new controls use this scale.
    public int SortingPriority = 1;
    public bool MatchAny;
    public PneumaticFilterRule[] Rules = Array.Empty<PneumaticFilterRule>();
}

internal sealed class PneumaticRouterConfiguration
{
    public int Version = 3;
    public int Revision;
    public string Policy = "priority";
    public PneumaticPortSettings[] Ports = Enumerable.Range(0, 4).Select(_ => new PneumaticPortSettings()).ToArray();

    public bool Valid => Version == 3 && Revision >= 0 &&
        (Policy is "priority" or "round-robin" or "forced-round-robin") && Ports?.Length == 4 &&
        Ports.All(p => p != null && (p.Role is "auto" or "input" or "output" or "closed") &&
            p.Priority >= -10 && p.Priority <= 10 && p.SortingPriority >= 1 && p.SortingPriority <= 3 && p.Rules != null && p.Rules.Length <= 8 &&
            p.Rules.All(r => r != null && PneumaticRouterFilters.Kinds.Contains(r.Kind) &&
                r.Value != null && r.Value.Length <= 120 && r.ValidExpression));

    internal static PneumaticRouterConfiguration? Parse(string text)
    {
        if (text.Length > 16384) return null;
        try
        {
            using var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 16 };
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) return null;
            bool Types(JObject o, params (string Key, JTokenType Type)[] fields) => fields.All(f =>
                o.Properties().Where(p => p.Name.Equals(f.Key, StringComparison.OrdinalIgnoreCase)).All(p => p.Name == f.Key && p.Value.Type == f.Type));
            if (!Types(json, ("Version", JTokenType.Integer), ("Revision", JTokenType.Integer), ("Policy", JTokenType.String), ("Ports", JTokenType.Array))) return null;
            if (json["Ports"] is JArray ports)
                foreach (var token in ports)
                {
                    if (token is not JObject p || !Types(p, ("Role", JTokenType.String), ("Priority", JTokenType.Integer), ("SortingPriority", JTokenType.Integer),
                        ("MatchAny", JTokenType.Boolean), ("Rules", JTokenType.Array))) return null;
                    if (p["Rules"] is JArray rules)
                        foreach (var rule in rules)
                            if (rule is not JObject r || !Types(r, ("Kind", JTokenType.String), ("Value", JTokenType.String),
                                ("ValueExpression", JTokenType.String), ("Exclude", JTokenType.Boolean))) return null;
                }
            var result = json.ToObject<PneumaticRouterConfiguration>();
            if (result?.Version == 1)
            {
                // Sequential 1 -> 2 migration. Retain the original fields and
                // compress the distinct ranks into the three output levels.
                if (result.Ports?.Length != 4 || result.Ports.Any(p => p == null || p.Priority < -10 || p.Priority > 10)) return null;
                var ranks = result.Ports.Select(p => p.Priority).Distinct().OrderBy(v => v).ToArray();
                foreach (var p in result.Ports) p.SortingPriority = Math.Max(1, 3 - (ranks.Length - 1 - Array.IndexOf(ranks, p.Priority)));
                result.Version = 2;
            }
            if (result?.Version == 2)
            {
                // Sequential 2 -> 3 migration. An old Value containing AND,
                // OR or parentheses must keep matching that exact literal.
                if (result.Ports?.Length != 4 || result.Ports.Any(p => p == null || p.Rules == null || p.Rules.Any(r => r == null))) return null;
                foreach (var rule in result.Ports.SelectMany(p => p.Rules)) rule.ValueExpression = PneumaticFilterExpression.Atom(rule.Value);
                result.Version = 3;
            }
            return result?.Valid == true ? result : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>The same predicates are used before extraction and at live router handoffs.</summary>
internal static class PneumaticRouterFilters
{
    internal static readonly string[] Kinds = { "identity", "mod", "item", "block", "material", "tool",
        "food", "fuel", "smeltable", "stackable", "durability", "code", "attribute" };

    internal static ItemStack[] Samples(IEnumerable<IInventory> inventories) => inventories.Where(i => i != null)
        .SelectMany(i => i).Take(512).Where(s => !s.Empty && s.Itemstack?.Collectible.Code != null).Select(s => s.Itemstack!)
        .DistinctBy(s => (s.Class, s.Collectible.Code.ToString(), s.Attributes.GetInt("durability", s.Collectible.GetMaxDurability(s))))
        .Take(128).Select(s => s.Clone()).ToArray();
    internal static string SampleValue(string kind, ItemStack stack) => kind switch {
        "mod" => stack.Collectible.Code.Domain,
        "material" => Materials(stack).FirstOrDefault() ?? "",
        "tool" => stack.Collectible.Tool?.ToString() ?? "",
        "durability" => stack.Collectible.GetMaxDurability(stack) is int max && max > 0
            ? Math.Clamp((int)(stack.Attributes.GetInt("durability", max) * 100.0 / max), 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture) : "0",
        _ => stack.Collectible.Code.ToString() };
    internal static string SampleExpression(string kind, ItemStack stack) => PneumaticFilterExpression.Group(
        kind == "material" ? Materials(stack) : new[] { SampleValue(kind, stack) });
    internal static bool UsesValues(string kind) => kind is "identity" or "mod" or "material" or "tool" or "durability" or "code" or "attribute";

    internal static IEnumerable<string> Materials(ItemStack stack)
    {
        if (stack.Block != null) yield return "block:" + stack.Block.BlockMaterial.ToString().ToLowerInvariant();
        var c = stack.Collectible;
        foreach (string key in new[] { "material", "metal", "wood", "rock", "stone", "clay", "leather", "fabric" })
        {
            if (c.Variant != null && c.Variant.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
                yield return key + ":" + value;
        }
        string? material = c.Attributes?["material"].AsString();
        if (!string.IsNullOrEmpty(material)) yield return "material:" + material;
    }

    internal static bool Match(PneumaticPortSettings port, ItemStack stack)
    {
        if (port.Rules.Length == 0) return true;
        if (port.Rules.Any(rule => rule.Exclude && Match(rule, stack))) return false;
        var includes = port.Rules.Where(rule => !rule.Exclude).ToArray();
        return includes.Length == 0 || (port.MatchAny ? includes.Any(rule => Match(rule, stack)) : includes.All(rule => Match(rule, stack)));
    }

    internal static bool Match(PneumaticFilterRule rule, ItemStack stack)
    {
        if (!UsesValues(rule.Kind) || rule.ValueExpression == "") return MatchValue(rule.Kind, rule.Value, stack);
        return rule.Expression?.Match(value => MatchValue(rule.Kind, value, stack)) == true;
    }
    private static bool MatchValue(string kind, string value, ItemStack stack)
    {
        var c = stack.Collectible;
        if (c?.Code == null) return false;
        return kind switch
        {
            "identity" => c.Code.ToString() == value,
            "mod" => c.Code.Domain == value,
            "item" => stack.Class == EnumItemClass.Item,
            "block" => stack.Class == EnumItemClass.Block,
            "material" => Materials(stack).Contains(value, StringComparer.OrdinalIgnoreCase),
            "tool" => c.Tool != null && (value == "" || c.Tool.ToString()!.Equals(value, StringComparison.OrdinalIgnoreCase)),
            "food" => c.NutritionProps != null,
            "fuel" => c.CombustibleProps?.BurnDuration > 0,
            "smeltable" => c.CombustibleProps?.SmeltedStack != null,
            "stackable" => c.MaxStackSize > 1,
            "durability" => c.GetMaxDurability(stack) > 0 && int.TryParse(value, out int percent) &&
                percent >= 0 && percent <= 100 && stack.Attributes.GetInt("durability", c.GetMaxDurability(stack)) * 100.0 /
                    c.GetMaxDurability(stack) >= percent,
            "code" => Wildcard(value, c.Code.ToString()),
            "attribute" => value.Length > 0 && (stack.Attributes.HasAttribute(value) || c.Attributes?[value].Exists == true),
            _ => false
        };
    }

    // Bounded wildcard matcher; no regex backtracking or script predicates.
    internal static bool Wildcard(string pattern, string value)
    {
        int p = 0, v = 0, star = -1, retry = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v])) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
            else if (star >= 0) { p = star + 1; v = ++retry; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
