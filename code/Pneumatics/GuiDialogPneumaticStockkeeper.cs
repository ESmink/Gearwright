using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Gearwright.Pneumatics;

internal sealed class GuiDialogPneumaticStockkeeper : GuiDialogBlockEntity
{
    private readonly BlockEntityPneumaticTransport host;
    private readonly StockkeeperEdit edit;
    private readonly InventoryGeneric ghosts;
    private bool composing;
    private static string T(string key, params object[] args) => Lang.Get("gearwright:stockkeeper-" + key, args);
    internal GuiDialogPneumaticStockkeeper(BlockEntityPneumaticTransport host, ICoreClientAPI api)
        : base(T("title"), host.Pos, api)
    {
        this.host = host;
        edit = new() { Revision = host.Stockkeeper.Revision, Rows = host.Stockkeeper.Rows.Select(r => new StockkeeperRowEdit { Target = r.Target }).ToArray() };
        ghosts = new InventoryGeneric(4, "gearwright-stockkeeper-samples", api) { PutLocked = true, TakeLocked = true };
        for (int i = 0; i < 4; i++) ghosts[i].Itemstack = host.Stockkeeper.Rows[i].Sample?.Clone();
        Compose();
    }
    private void Compose()
    {
        composing = true;
        var choices = new List<(string Code, string Name, ItemStack? Stack)> { ("keep", T("keep-sample"), null), ("clear", T("clear-sample"), null) };
        foreach (string source in new[] { "hotbar", "backpack" })
        {
            var inventory = capi.World.Player.InventoryManager.GetOwnInventory(source);
            if (inventory == null) continue;
            for (int n = 0; n < inventory.Count; n++)
                if (inventory[n].Itemstack is { } stack && PneumaticInventory.Supported(stack))
                    choices.Add((source + ":" + n, stack.GetName(), stack.Clone()));
        }
        var background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        background.BothSizing = ElementSizing.FitToChildren;
        background.WithChildren(ElementBounds.Fixed(0, 28, 710, 535));
        var c = capi.Gui.CreateCompo("gearwright-stockkeeper-" + host.Pos, ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle))
            .AddShadedDialogBG(background, true).AddDialogTitleBar(T("title"), () => TryClose()).BeginChildElements(background)
            .AddDynamicText(InventoryName(), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 35, 700, 25), "inventory")
            .AddStaticText(T("help"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 65, 700, 43));
        for (int n = 0; n < 4; n++)
        {
            int row = n; double y = 116 + n * 93;
            c.AddItemSlotGrid(ghosts, _ => { }, 1, new[] { n }, ElementBounds.Fixed(0, y, 48, 48), "sample" + n)
                .AddDropDown(choices.Select(s => s.Code).ToArray(), choices.Select(s => s.Name).ToArray(), 0,
                    (value, on) =>
                    {
                        if (!on || composing) return;
                        var change = edit.Rows[row]; var choice = choices.First(s => s.Code == value);
                        if (value is "keep" or "clear") { change.Source = value; ghosts[row].Itemstack = value == "keep" ? host.Stockkeeper.Rows[row].Sample?.Clone() : null; }
                        else { var parts = value.Split(':'); change.Source = parts[0]; change.Slot = int.Parse(parts[1], CultureInfo.InvariantCulture); ghosts[row].Itemstack = choice.Stack!.Clone(); ghosts[row].Itemstack!.StackSize = 1; }
                        ghosts[row].MarkDirty();
                    }, ElementBounds.Fixed(60, y, 295, 30), "choice" + n)
                .AddStaticText(T("keep"), CairoFont.WhiteSmallText(), ElementBounds.Fixed(375, y + 4, 54, 25))
                .AddTextInput(ElementBounds.Fixed(432, y, 117, 30), _ => { }, CairoFont.WhiteSmallText(), "target" + n)
                .AddButton("−", () => Adjust(row, -1), ElementBounds.Fixed(560, y, 48, 30))
                .AddButton("+", () => Adjust(row, 1), ElementBounds.Fixed(617, y, 48, 30))
                .AddDynamicText(Progress(n), CairoFont.WhiteSmallText(), ElementBounds.Fixed(60, y + 37, 620, 48), "progress" + n);
        }
        c.AddDynamicText("", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 495, 540, 45), "error")
            .AddButton(Lang.Get("gearwright:apply"), Apply, ElementBounds.Fixed(575, 496, 120, 34));
        SingleComposer = c.EndChildElements().Compose();
        for (int i = 0; i < 4; i++) SingleComposer.GetTextInput("target" + i).SetValue(edit.Rows[i].Target.ToString(CultureInfo.InvariantCulture));
        composing = false; Refresh();
    }
    private string InventoryName() => host.Chest is { } chest ? T("inventory", new ItemStack(chest.Chest.Block).GetName()) : T("no-inventory");
    private string Progress(int row)
    {
        var r = host.Stockkeeper.Rows[row];
        return T("progress", r.Stored, (long)r.Reserved + r.Incoming, r.Needed) + "\n" + T("status-" + r.Status);
    }
    internal void Refresh()
    {
        if (SingleComposer == null || !IsOpened()) return;
        SingleComposer.GetDynamicText("inventory").SetNewText(InventoryName());
        for (int i = 0; i < 4; i++)
        {
            SingleComposer.GetDynamicText("progress" + i).SetNewText(Progress(i));
        }
    }
    private bool Adjust(int row, int delta)
    {
        var input = SingleComposer.GetTextInput("target" + row);
        if (int.TryParse(input.GetText(), out int value)) input.SetValue(Math.Clamp((long)value + delta, 0, PneumaticStockkeeperState.MaximumTarget).ToString(CultureInfo.InvariantCulture));
        return true;
    }
    private bool Apply()
    {
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(SingleComposer.GetTextInput("target" + i).GetText(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
                value < 0 || value > PneumaticStockkeeperState.MaximumTarget)
            { SingleComposer.GetDynamicText("error").SetNewText(T("invalid-target")); return true; }
            edit.Rows[i].Target = value;
            for (int j = 0; j < i; j++) if (!ghosts[i].Empty && PneumaticStockkeeperState.Matches(capi.World, ghosts[j].Itemstack, ghosts[i].Itemstack!))
            { SingleComposer.GetDynamicText("error").SetNewText(T("duplicate-sample")); return true; }
        }
        host.SendStockkeeperConfiguration(edit); TryClose(); return true;
    }
}
