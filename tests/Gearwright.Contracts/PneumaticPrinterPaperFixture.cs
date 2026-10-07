using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cairo;
using Gearwright.Pneumatics;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using static Gearwright.Contracts.PumpRuntimeFixture;
using Path = System.IO.Path;

namespace Gearwright.Contracts;

internal static class PneumaticPrinterPaperFixture
{
    private static readonly TextureAtlasPosition WholeTexture = new() { x1 = 0, y1 = 0, x2 = 1, y2 = 1 };
    private static readonly (string Name, int Amount)[] Examples = {
        ("Copper ingot", 8), ("Tin bronze plate", 3), ("Granite stone", 1),
        ("Cupronickel plate", 8), ("Flax fibers", 5), ("Oak planks", 8),
        ("Iron ingot", 8), ("Copper ingot", 4), ("Oak planks", 2), ("Flax fibers", 1), ("Granite stone", 7)
    };

    internal static void Run(Action<bool, string> check)
    {
        check(PneumaticPrinterPaper.Prefix("A\u0308pfel", 1, s => new System.Globalization.StringInfo(s).LengthInTextElements) == "A\u0308" &&
            PneumaticPrinterPaper.Prefix("🪵 Logs", 1, s => new System.Globalization.StringInfo(s).LengthInTextElements) == "🪵",
            "Receipt truncation never splits a combining mark or surrogate pair");
        using var surface = new ImageSurface(Format.Argb32, 256, 528);
        using var ctx = new Context(surface);
        bool fitted = true;
        foreach (var name in new[] { "Copper ingot", "Cupronickel plate", "Geschliffener Kalkstein", "Медный слиток", "A\u0308pfel" })
            foreach (int amount in new[] { 1, 8, 65535 })
            {
                var label = PneumaticPrinterPaper.DrawLabel(ctx, name, amount, 0);
                fitted &= label.Name.Length > 0 && name.StartsWith(label.Name, StringComparison.Ordinal) &&
                    label.Amount == amount.ToString() && label.NameEnd < label.AmountStart - 14;
            }
        check(fitted, "Native font measurement fits localized name prefixes, a smaller x and the full quantity without overlap");
        var path = PneumaticPrinterPaper.Path;
        double half = .8 * Math.Tan(Math.PI / 8);
        check(Math.Abs(path[7].X - (3.8 - half)) < 1e-9 && Math.Abs(path[8].X - (12.2 + half)) < 1e-9 &&
            path[7].Y == path[8].Y && Math.Abs(path[8].S - (path[8].X - 5.25)) < 1e-9,
            "Continuous paper path meets both octagonal roll tops at the exact web tangents");
        var grain = PneumaticPrinterPaper.Mesh(30); var ink = PneumaticPrinterPaper.Mesh(22);
        bool bounded = true, wrapped = false, scrolling = false;
        double[]? initialUv = null;
        for (int step = 0; step <= 330; step++)
        {
            double phase = step / 10d;
            PneumaticPrinterPaper.WriteGrain(grain, WholeTexture, phase);
            bounded &= grain.VerticesCount <= 120 && grain.Uv.Take(grain.VerticesCount * 2).All(v => float.IsFinite(v) && v >= -1e-6 && v <= 1 + 1e-6);
            if (step == 0) initialUv = grain.Uv.Take(grain.VerticesCount * 2).Select(v => (double)v).ToArray();
            if (step == 5) scrolling = !grain.Uv.Take(initialUv!.Length).Select(v => (double)v).SequenceEqual(initialUv);
            ink.VerticesCount = 0;
            for (int age = -1; age < 10; age++)
            {
                int before = ink.VerticesCount;
                PneumaticPrinterPaper.WriteLabel(ink, PneumaticPrinterPaper.LabelPosition(age, phase % 1.35), age + 1);
                bounded &= ink.VerticesCount - before <= 8;
            }
            bounded &= ink.VerticesCount <= 88 && ink.xyz.Take(ink.VerticesCount * 3).All(float.IsFinite);
            wrapped |= Enumerable.Range(0, ink.VerticesCount).Any(i => ink.xyz[i * 3] > 12.2 / 16 && ink.xyz[i * 3 + 1] < 14.0 / 16);
        }
        check(bounded && scrolling && wrapped, "Grain scrolls within its atlas tile; receipts bend down the take-up reel within fixed mesh budgets");
        ink.VerticesCount = 0;
        PneumaticPrinterPaper.WriteLabel(ink, path[8].S, 0);
        // Shared vertices at the fold use the same texture coordinates. The tiny
        // surface lift follows each face normal to avoid z-fighting with the reel.
        check(ink.VerticesCount == 8 && Math.Abs(ink.Uv[5] - ink.Uv[9]) < 1e-6 &&
            ink.Flags[0] != ink.Flags[4], "A printed line straddles the roll corner with continuous UVs and the correct two surface normals");
        check(ink.Uv[0] == 1 && ink.Uv[2] == 0 && ink.xyz[2] < ink.xyz[5],
            "Text reads left to right from the take-up end rather than being mirrored on the paper");
        check(PneumaticPrinterPaper.LabelPosition(-1, 1.35) == PneumaticPrinterPaper.LabelPosition(0, 0) &&
            PneumaticPrinterPaper.ReplacedElement("stock-print-ink-0") && PneumaticPrinterPaper.ReplacedElement("stock-print-history-3"),
            "Finishing an order keeps the printed line in place and runtime rendering excludes the old dot stamps");
        Renderer(check);
    }

    private sealed class PaperMesh : MeshRef
    {
        public override bool Initialized => true;
    }
    private sealed class NamedItem : Item
    {
        internal int NameReads;
        public override string GetHeldItemName(ItemStack stack)
        { NameReads++; return "Fine ingot " + stack.Attributes.GetString("finish", "plain"); }
    }

    private static void Renderer(Action<bool, string> check)
    {
        int uploads = 0, updates = 0, textures = 0, draws = 0, deletes = 0;
        bool bounded = true, blend = false;
        var meshes = new List<PaperMesh>(); var bindings = new List<int>();
        var render = Stub.Create<IRenderAPI>((method, args) =>
        {
            if (method.Name == "UploadMesh") { uploads++; var mesh = new PaperMesh(); meshes.Add(mesh); return mesh; }
            if (method.Name == "UpdateMesh")
            {
                updates++; var mesh = (MeshData)args![1]!;
                bounded &= mesh.VerticesCount is 88 or 120 && mesh.IndicesCount == 0 && mesh.Indices == null;
            }
            if (method.Name == "RenderMesh") draws++;
            if (method.Name == "GlToggleBlend") blend = (bool)args![0]!;
            return null;
        });
        var gui = Stub.Create<IGuiAPI>((method, args) =>
        {
            if (method.Name == "LoadOrUpdateCairoTexture") { textures++; ((LoadedTexture)args![2]!).TextureId = 42; }
            if (method.Name == "DeleteTexture") deletes++;
            return null;
        });
        var api = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch { "get_Render" => render, "get_Gui" => gui, _ => null });
        var shader = Stub.Create<IStandardShaderProgram>((method, args) =>
        { if (method.Name == "set_Tex2D") bindings.Add((int)args![0]!); return null; });
        var item = new NamedItem { Code = new("game:ingot-test") };
        var stack = new ItemStack(item, 8); stack.Attributes.SetString("finish", "polished");
        var state = new PneumaticStockkeeperState(); state.BeginOrder(Guid.NewGuid().ToString("N"), stack);
        using (var printer = new PneumaticPrinterPaperRenderer(api, new TextureAtlasPosition { x2 = 1, y2 = 1, atlasTextureId = 17 }))
        {
            printer.Render(state, .5, shader);
            int stalledUpdates = updates;
            for (int i = 0; i < 20; i++) printer.Render(state, .5, shader);
            check(updates == stalledUpdates && textures == 1 && item.NameReads == 1,
                "Paused paper reuses its mesh and text atlas; item names come from the actual stack's display-name method");
            printer.Render(state, 80 / 120d, shader);
            check(updates == stalledUpdates + 2 && textures == 1,
                "Feeding updates paper and receipt vertices/UVs together without uploading text again");
            for (int tick = 0; tick < 40; tick++) state.AdvancePrint(.1);
            printer.Render(state, 1, shader);
            state.BeginOrder(Guid.NewGuid().ToString("N"), new ItemStack(item, 3));
            printer.Render(state, 0, shader);
            check(uploads == 2 && bounded && textures == 3 && !blend && draws == bindings.Count &&
                bindings.Where((_, i) => i % 2 == 0).All(t => t == 17) && bindings.Where((_, i) => i % 2 == 1).All(t => t == 42),
                "The live paper renderer uses bounded reusable meshes, the native paper atlas and its receipt texture, then restores blending");
        }
        check(meshes.All(m => m.Disposed) && deletes == 1, "Unloading a printer releases both meshes and its generated text texture");
    }

    internal static void Preview(string directory)
    {
        Directory.CreateDirectory(directory);
        var labels = new List<object>();
        using (var surface = new ImageSurface(Format.Argb32, 256, 528))
        using (var ctx = new Context(surface))
        {
            ctx.SetSourceRGBA(0, 0, 0, 0); ctx.Operator = Operator.Source; ctx.Paint(); ctx.Operator = Operator.Over;
            for (int row = 0; row < Examples.Length; row++)
            {
                var fitted = PneumaticPrinterPaper.DrawLabel(ctx, Examples[row].Name, Examples[row].Amount, row);
                labels.Add(new { name = fitted.Name, amount = fitted.Amount });
            }
            surface.WriteToPng(Path.Combine(directory, "labels.png"));
        }
        object Export(MeshData mesh) => new { xyz = mesh.xyz.Take(mesh.VerticesCount * 3), uv = mesh.Uv.Take(mesh.VerticesCount * 2),
            indices = mesh.Indices.Take(mesh.VerticesCount / 4 * 6) };
        var frames = new List<object>();
        foreach (int frame in new[] { 60, 80, 100 })
        {
            double feed = PneumaticPrinterMotion.Pose(frame / 120d).Feed;
            var grain = PneumaticPrinterPaper.Mesh(30); PneumaticPrinterPaper.WriteGrain(grain, WholeTexture, 8.1 + feed);
            var ink = PneumaticPrinterPaper.Mesh(22); ink.VerticesCount = 0;
            for (int row = 0; row < Examples.Length; row++) PneumaticPrinterPaper.WriteLabel(ink, row * 1.35 + feed, row);
            frames.Add(new { frame, feed, grain = Export(grain), ink = Export(ink) });
        }
        File.WriteAllText(Path.Combine(directory, "paper-mesh.json"), JsonConvert.SerializeObject(new { labels, frames }, Formatting.Indented));
    }
}
