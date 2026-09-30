using System;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Gearwright.Pneumatics;

/// <summary>Numbered physical port map, sample-based predicates and per-port rule cards.</summary>
internal sealed class GuiDialogPneumaticRouter : GuiDialogBlockEntity
{
    private readonly BlockEntityPneumaticTransport host;
    private readonly PneumaticRouterConfiguration edit;
    private int selected, ruleIndex;
    private ItemStack? sample;
    private bool composing;
    private static string T(string key) => Lang.Get("gearwright:router-" + key);
    internal GuiDialogPneumaticRouter(BlockEntityPneumaticTransport host, ICoreClientAPI api, int port)
        : base(T("title"), host.Pos, api)
    { this.host = host; selected = port; edit = JsonConvert.DeserializeObject<PneumaticRouterConfiguration>(JsonConvert.SerializeObject(host.Router.Configuration))!; Compose(); }
    private PneumaticPortSettings Port => edit.Ports[selected - 1];
    private PneumaticFilterRule? Rule => Port.Rules.ElementAtOrDefault(ruleIndex);
    private string Values => Rule == null ? "" : Rule.ValueExpression == "" ? PneumaticFilterExpression.Atom(Rule.Value) : Rule.ValueExpression;
    private void SetExpression(string value)
    {
        if (composing || Rule == null) return;
        Rule.ValueExpression = value == "" ? PneumaticFilterExpression.Atom("") : value;
        SingleComposer.GetDynamicText("expression-status").SetNewText(Rule.ValidExpression ? "" : T("expression-invalid"));
    }
    private bool Apply()
    {
        for (int p = 0; p < 4; p++)
            for (int r = 0; r < edit.Ports[p].Rules.Length; r++)
                if (!edit.Ports[p].Rules[r].ValidExpression) { selected = p + 1; ruleIndex = r; Compose(); return true; }
        if (System.Text.Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(edit)) > 16384)
        { SingleComposer.GetDynamicText("apply-status").SetNewText(T("expression-limit")); return true; }
        if (!edit.Valid) return true;
        host.SendRouterConfiguration(edit); TryClose(); return true;
    }
    private void Select(int port) { selected = port; ruleIndex = 0; Compose(); }
    private void Compose()
    {
        composing = true; host.SelectedRouterPort = selected;
        SingleComposer?.Dispose();
        var background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        var extent = ElementBounds.Fixed(0, 28, 790, 505); background.WithChildren(extent);
        var bounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(0, -20);
        var c = capi.Gui.CreateCompo("gearwright-router-" + host.Pos, bounds).AddShadedDialogBG(background, true)
            .AddDialogTitleBar(T("title"), () => TryClose()).BeginChildElements(background)
            .AddStaticText(T("physical-ports"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 35, 320, 44))
            .AddStaticCustomDraw(ElementBounds.Fixed(0, 85, 310, 245), (ctx, surface, b) =>
            {
                double cx = b.drawX + b.InnerWidth / 2, cy = b.drawY + b.InnerHeight / 2;
                ctx.SetSourceRGBA(.16, .13, .09, 1); ctx.Rectangle(b.drawX, b.drawY, b.InnerWidth, b.InnerHeight); ctx.Fill();
                ctx.LineWidth = 12;
                foreach (var p in new[] { (1, -1, 0), (2, 0, 1), (3, 1, 0), (4, 0, -1) })
                {
                    string role = host.PortRole(p.Item1);
                    bool connected = host.Connected(p.Item1), inlet = role == "input";
                    ctx.SetSourceRGBA(p.Item1 == selected ? .95 : connected && inlet ? .35 : .45,
                        p.Item1 == selected ? .72 : connected && inlet ? .65 : .34, connected && inlet ? .60 : .15, connected ? 1 : .35);
                    ctx.MoveTo(cx + p.Item2 * 48, cy + p.Item3 * 48); ctx.LineTo(cx + p.Item2 * 105, cy + p.Item3 * 88); ctx.Stroke();
                    if (connected && role is "input" or "output")
                    {
                        ctx.SetSourceRGBA(.08, .08, .07, 1);
                        int direction = inlet ? -1 : 1;
                        double ax = cx + p.Item2 * 60, ay = cy + p.Item3 * 60;
                        ctx.LineWidth = 2;
                        ctx.MoveTo(ax - p.Item2 * 5 * direction - p.Item3 * 4, ay - p.Item3 * 5 * direction + p.Item2 * 4);
                        ctx.LineTo(ax + p.Item2 * 5 * direction, ay + p.Item3 * 5 * direction);
                        ctx.LineTo(ax - p.Item2 * 5 * direction + p.Item3 * 4, ay - p.Item3 * 5 * direction - p.Item2 * 4); ctx.Stroke();
                        ctx.LineWidth = 12;
                    }
                }
                ctx.LineWidth = 5; ctx.SetSourceRGBA(.42, .65, .57, 1); ctx.Arc(cx, cy, 40, 0, Math.PI * 2); ctx.Stroke();
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    ctx.MoveTo(cx + Math.Cos(angle) * 35, cy + Math.Sin(angle) * 35);
                    ctx.LineTo(cx + Math.Cos(angle) * 48, cy + Math.Sin(angle) * 48); ctx.Stroke();
                }
                ctx.MoveTo(cx - 22, cy); ctx.LineTo(cx + 22, cy); ctx.MoveTo(cx, cy - 22); ctx.LineTo(cx, cy + 22); ctx.Stroke();
            });
        var buttons = new[] { (1, 0, 185), (2, 111, 290), (3, 222, 185), (4, 111, 90) };
        foreach (var p in buttons)
        { int port = p.Item1; c.AddButton(T("port") + " " + port, () => { Select(port); return true; }, ElementBounds.Fixed(p.Item2, p.Item3, 88, 30)); }
        c.AddStaticText(T("policy"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 350, 310, 25));
        string[] policies = { "priority", "round-robin", "forced-round-robin" };
        c.AddDropDown(policies, policies.Select(T).ToArray(), Array.IndexOf(policies, edit.Policy),
            (value, on) => { if (on && !composing) edit.Policy = value; }, ElementBounds.Fixed(0, 378, 310, 32), "policy");
        c.AddStaticText(T("policy-help"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 422, 310, 80));
        c.AddStaticText(T("port") + " " + selected + " • " + T(host.Connected(selected) ? host.PortRole(selected) : "disconnected"),
            CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 35, 420, 25));
        bool output = host.Connected(selected) && host.PortRole(selected) == "output";
        c.AddStaticText(T(output ? "pipe-role" : "input-help"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 67, output ? 180 : 425, 60));
        if (output)
        {
        c.AddStaticText(T("output-priority"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(545, 40, 220, 24));
        c.AddSlider(value => { Port.SortingPriority = value; return true; }, ElementBounds.Fixed(545, 70, 220, 25), "priority");
        c.AddStaticText(Port.Rules.Length == 0 ? T("any") : T("rules"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 118, 240, 26));
        c.AddButton(T("add-rule"), () => { if (Port.Rules.Length < 8) { Port.Rules = Port.Rules.Append(new PneumaticFilterRule { ValueExpression = PneumaticFilterExpression.Atom("") }).ToArray(); ruleIndex = Port.Rules.Length - 1; Compose(); } return true; }, ElementBounds.Fixed(640, 112, 125, 30));
        if (Rule != null)
        {
            string[] values = Enumerable.Range(0, Port.Rules.Length).Select(i => i.ToString()).ToArray();
            c.AddDropDown(values, Port.Rules.Select((r, i) => (i + 1) + ". " + T("filter-" + r.Kind)).ToArray(), ruleIndex,
                (value, on) => { if (on && !composing) { ruleIndex = int.Parse(value); Compose(); } }, ElementBounds.Fixed(340, 153, 315, 32), "rule-list");
            c.AddButton(T("remove"), () => { Port.Rules = Port.Rules.Where((_, i) => i != ruleIndex).ToArray(); ruleIndex = Math.Max(0, ruleIndex - 1); Compose(); return true; }, ElementBounds.Fixed(665, 153, 100, 32));
            c.AddDropDown(PneumaticRouterFilters.Kinds, PneumaticRouterFilters.Kinds.Select(k => T("filter-" + k)).ToArray(),
                Array.IndexOf(PneumaticRouterFilters.Kinds, Rule.Kind), (value, on) => { if (on && !composing && Rule != null) { Rule.Kind = value;
                    if (sample != null && PneumaticRouterFilters.UsesValues(value)) Rule.ValueExpression = PneumaticRouterFilters.SampleExpression(value, sample); Compose(); } },
                ElementBounds.Fixed(340, 207, 315, 32), "kind");
            c.AddSwitch(on => { if (Rule != null) Rule.Exclude = on; }, ElementBounds.Fixed(675, 210, 40, 28), "exclude");
            c.AddStaticText(T("exclude"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(670, 244, 100, 25));
            c.AddStaticText(T("expression-help"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 244, 315, 26));
            c.AddStaticText(T("filter-help-" + Rule.Kind), CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 274, 425, 58));
            c.AddTextInput(ElementBounds.Fixed(340, 338, 425, 30), SetExpression, CairoFont.WhiteSmallText(), "value");
            c.AddDynamicText(Rule.ValidExpression ? "" : T("expression-invalid"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 371, 425, 17), "expression-status");
            var inventories = capi.World.Player.InventoryManager;
            var samples = PneumaticRouterFilters.Samples(new[] { inventories.GetOwnInventory("hotbar"), inventories.GetOwnInventory("backpack") });
            c.AddDropDown(new[] { "choose" }.Concat(Enumerable.Range(0, samples.Length).Select(i => i.ToString())).ToArray(),
                new[] { T("sample-inventory") }.Concat(samples.Select(s => s.GetName() + (s.Collectible.GetMaxDurability(s) > 0 ?
                    " (" + PneumaticRouterFilters.SampleValue("durability", s) + "%)" : ""))).ToArray(), 0,
                (value, on) => { if (on && !composing && value != "choose" && Rule != null) { sample = samples[int.Parse(value)]; Rule.ValueExpression = PneumaticRouterFilters.SampleExpression(Rule.Kind, sample); Compose(); } },
                ElementBounds.Fixed(340, 389, 200, 32), "inventory-sample");
            if (Rule.Kind == "material")
            {
                var tags = (sample == null ? Array.Empty<string>() : PneumaticRouterFilters.Materials(sample)).Distinct().ToArray();
                if (tags.Length > 0)
                {
                    string group = PneumaticFilterExpression.Group(tags);
                    int match = Array.FindIndex(tags, t => PneumaticFilterExpression.Atom(t) == Values);
                    int choice = Values == group ? 0 : match >= 0 ? match + 1 : tags.Length + 1;
                    c.AddDropDown(new[] { "all" }.Concat(Enumerable.Range(0, tags.Length).Select(i => i.ToString())).Append("custom").ToArray(),
                        new[] { group }.Concat(tags).Append(T("expression-custom")).ToArray(), choice,
                        (value, on) => { if (on && !composing && Rule != null && value != "custom") {
                            Rule.ValueExpression = value == "all" ? group : PneumaticFilterExpression.Atom(tags[int.Parse(value)]);
                            SingleComposer.GetTextInput("value").SetValue(Rule.ValueExpression);
                            SingleComposer.GetDynamicText("expression-status").SetNewText(Rule.ValidExpression ? "" : T("expression-invalid")); } },
                        ElementBounds.Fixed(340, 429, 425, 28), "material-sample");
                }
            }
            c.AddSwitch(on => Port.MatchAny = on, ElementBounds.Fixed(560, 391, 40, 28), "match-any");
            c.AddStaticText(T("match-any"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(605, 393, 165, 25));
        }
        }
        c.AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(340, 465, 290, 45), "apply-status");
        c.AddButton(Lang.Get("gearwright:apply"), Apply, ElementBounds.Fixed(645, 465, 120, 34));
        SingleComposer = c.EndChildElements().Compose();
        if (output) SingleComposer.GetSlider("priority").SetValues(Port.SortingPriority, 1, 3, 1);
        if (output && Rule != null)
        {
            SingleComposer.GetTextInput("value").SetValue(Values);
            SingleComposer.GetSwitch("exclude").SetValue(Rule.Exclude);
            SingleComposer.GetSwitch("match-any").SetValue(Port.MatchAny);
        }
        composing = false;
    }
    public override void OnGuiClosed() { host.SelectedRouterPort = 0; base.OnGuiClosed(); }
}
