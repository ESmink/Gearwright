using System;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace Gearwright.Pneumatics;

/// <summary>Bounded meshes and one small text atlas per loaded printer; no frame-by-frame text uploads.</summary>
internal sealed class PneumaticPrinterPaperRenderer : IDisposable
{
    private const int GrainVertices = 30 * 4, InkVertices = 22 * 4;
    private static readonly PneumaticStockkeeperState Blank = new();
    private readonly ICoreClientAPI api;
    private readonly TextureAtlasPosition paper;
    private readonly MeshData grain = PneumaticPrinterPaper.Mesh(30), ink = PneumaticPrinterPaper.Mesh(22);
    private readonly MeshRef grainMesh, inkMesh;
    private LoadedTexture text;
    private string labelsKey = "";
    private double previousPhase = double.NaN, previousFeed = double.NaN, previousReveal = double.NaN;
    private bool changed;

    internal PneumaticPrinterPaperRenderer(ICoreClientAPI api, TextureAtlasPosition paper)
    {
        this.api = api; this.paper = paper;
        grainMesh = api.Render.UploadMesh(grain); inkMesh = api.Render.UploadMesh(ink);
        text = new LoadedTexture(api);
        // Geometry updates retain the fixed GPU index buffers and vertex capacity.
        grain.Indices = ink.Indices = null; grain.IndicesCount = ink.IndicesCount = 0;
    }

    internal void Render(PneumaticStockkeeperState state, double progress, IStandardShaderProgram shader)
    {
        if (!state.Writable) state = Blank;
        string key = state.PrintId + ":" + state.Printing + ":" + Lang.CurrentLocale;
        if (labelsKey != key)
        {
            using var surface = new ImageSurface(Format.Argb32, PneumaticPrinterPaper.LabelWidth,
                PneumaticPrinterPaper.LabelPixels * (PneumaticStockkeeperState.HistoryLength + 1));
            using var ctx = new Context(surface);
            ctx.SetSourceRGBA(0, 0, 0, 0); ctx.Operator = Operator.Source; ctx.Paint(); ctx.Operator = Operator.Over;
            if (state.ActivePrint is { } active) PneumaticPrinterPaper.DrawLabel(ctx, active.Sample.GetName(), active.Amount, 0);
            for (int i = 0; i < state.History.Length; i++)
                PneumaticPrinterPaper.DrawLabel(ctx, state.History[i].Sample.GetName(), state.History[i].Amount, i + 1);
            api.Gui.LoadOrUpdateCairoTexture(surface, true, ref text);
            labelsKey = key; changed = true;
        }
        double feed = PneumaticPrinterPaper.Feed(state, progress), phase = state.PaperPhase + feed;
        double reveal = state.Printing ? Math.Clamp((Math.Floor((progress * 120 - 18) / 12) + 1) / 4, 0, 1) : 0;
        if (phase != previousPhase)
        {
            PneumaticPrinterPaper.WriteGrain(grain, paper, phase); PneumaticPrinterPaper.Pad(grain, GrainVertices);
            api.Render.UpdateMesh(grainMesh, grain); previousPhase = phase;
        }
        if (changed || feed != previousFeed || reveal != previousReveal)
        {
            ink.VerticesCount = 0;
            if (state.ActivePrint != null) PneumaticPrinterPaper.WriteLabel(ink, feed, 0, reveal);
            for (int i = 0; i < state.History.Length; i++)
                PneumaticPrinterPaper.WriteLabel(ink, PneumaticPrinterPaper.LabelPosition(i, feed), i + 1);
            PneumaticPrinterPaper.Pad(ink, InkVertices); api.Render.UpdateMesh(inkMesh, ink);
            changed = false; previousFeed = feed; previousReveal = reveal;
        }
        shader.Tex2D = paper.atlasTextureId; api.Render.RenderMesh(grainMesh);
        api.Render.GlToggleBlend(true, EnumBlendMode.Standard);
        try { shader.Tex2D = text.TextureId; api.Render.RenderMesh(inkMesh); }
        finally { api.Render.GlToggleBlend(false, EnumBlendMode.Standard); }
    }

    public void Dispose() { grainMesh.Dispose(); inkMesh.Dispose(); text.Dispose(); }
}
