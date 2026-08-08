using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Gearwright.Hydraulics;

public sealed class GuiDialogCreativeFluidPump : GuiDialogBlockEntity
{
    private const string LiquidKey = "liquid";
    private const string PressureKey = "pressure";
    private readonly BlockEntityCreativeFluidPump pump;
    private AssetLocation selectedLiquid;
    private int selectedPressure;

    public GuiDialogCreativeFluidPump(BlockEntityCreativeFluidPump pump, ICoreClientAPI capi)
        : base(Lang.Get("gearwright:creative-pump-title"), pump.Pos, capi)
    {
        this.pump = pump;
        selectedLiquid = pump.ConfiguredLiquidCode;
        selectedPressure = (int)Math.Round(pump.ConfiguredPressure);
        ComposeDialog(capi);
    }

    private void ComposeDialog(ICoreClientAPI capi)
    {
        Item[] liquids = capi.World.Items
            .Where(item => item?.Attributes?["waterTightContainerProps"].Exists == true)
            .OrderBy(item => item.Code.ToString(), StringComparer.Ordinal)
            .ToArray();
        string[] values = liquids.Select(item => item.Code.ToString()).ToArray();
        string[] names = liquids.Select(item => new ItemStack(item).GetName()).ToArray();
        int selectedIndex = Math.Max(0, Array.FindIndex(values, value => value == selectedLiquid.ToString()));

        ElementBounds dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(0, -40);
        ElementBounds background = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        ElementBounds liquidLabel = ElementBounds.Fixed(0, 35, 300, 25);
        ElementBounds liquidBounds = ElementBounds.Fixed(0, 62, 300, 35);
        ElementBounds pressureLabel = ElementBounds.Fixed(0, 108, 300, 25);
        ElementBounds pressureBounds = ElementBounds.Fixed(0, 137, 300, 30);
        ElementBounds applyBounds = ElementBounds.Fixed(200, 182, 100, 32);

        // Autosized dialogs need fixed-size descendants before their background is composed.
        // Without this relationship, the background resolves to 0x0 and Cairo rejects it.
        background.BothSizing = ElementSizing.FitToChildren;
        background.WithChildren(liquidLabel, liquidBounds, pressureLabel, pressureBounds, applyBounds);

        SingleComposer = capi.Gui.CreateCompo("gearwright-creative-fluid-pump" + BlockEntityPosition, dialogBounds)
            .AddShadedDialogBG(background, true)
            .AddDialogTitleBar(Lang.Get("gearwright:creative-pump-title"), () => TryClose())
            .BeginChildElements(background)
            .AddStaticText(Lang.Get("gearwright:creative-pump-liquid"), CairoFont.WhiteSmallText(), liquidLabel)
            .AddDropDown(values, names, selectedIndex, OnLiquidSelected, liquidBounds, LiquidKey)
            .AddStaticText(Lang.Get("gearwright:creative-pump-pressure"), CairoFont.WhiteSmallText(), pressureLabel)
            .AddSlider(OnPressureChanged, pressureBounds, PressureKey)
            .AddButton(Lang.Get("gearwright:apply"), Apply, applyBounds)
            .EndChildElements()
            .Compose();

        SingleComposer.GetSlider(PressureKey).SetValues(
            selectedPressure, 0, (int)HydraulicMath.MaximumCreativePressure, 10, " PU/s");
    }

    private void OnLiquidSelected(string code, bool selected)
    {
        if (!selected) return;
        try { selectedLiquid = new AssetLocation(code); }
        catch { }
    }

    private bool OnPressureChanged(int value)
    {
        selectedPressure = value;
        return true;
    }

    private bool Apply()
    {
        pump.SendConfiguration(selectedLiquid, selectedPressure);
        TryClose();
        return true;
    }
}
