using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Gearwright.Pneumatics;

/// <summary>The web and both octagonal reel skins share one material-distance coordinate.</summary>
internal static class PneumaticPrinterPaper
{
    internal const double Width = 2.8, Top = 14.16, PrintX = 5.25, LabelHeight = .55;
    internal const int LabelWidth = 256, LabelPixels = 48;
    internal readonly record struct Point(double S, double X, double Y);
    internal static readonly Point[] Path = MakePath();

    private static Point[] MakePath()
    {
        double r = PneumaticStockkeeperState.RollRadius, h = r * Math.Tan(Math.PI / 8);
        (double X, double Y)[] rim = { (h, r), (r, h), (r, -h), (h, -r), (-h, -r), (-r, -h), (-r, h), (-h, r) };
        var p = new List<Point>(); double s = 0;
        void Add(double x, double y)
        {
            if (p.Count > 0) s += Math.Sqrt(Math.Pow(x - p[^1].X, 2) + Math.Pow(y - p[^1].Y, 2));
            p.Add(new(s, x, y));
        }
        foreach (var v in rim) Add(3.8 + v.X, Top - r + v.Y);
        foreach (var v in rim) Add(12.2 + v.X, Top - r + v.Y);
        double origin = p[7].S + PrintX - p[7].X;
        return p.Select(v => v with { S = v.S - origin }).ToArray();
    }

    internal static double Feed(PneumaticStockkeeperState state, double progress) => state.Printing ? PneumaticPrinterMotion.Pose(progress).Feed : 0;
    internal static double LabelPosition(int historyIndex, double feed) => (historyIndex + 1) * PneumaticStockkeeperState.FeedDistance + feed;

    internal static string Prefix(string name, double available, System.Func<string, double> measure)
    {
        // Fit complete Unicode text elements; preserve the player's localized item name.
        name = string.Join(" ", name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var elements = StringInfo.GetTextElementEnumerator(name); string fitted = "";
        while (elements.MoveNext())
        {
            string next = fitted + elements.GetTextElement();
            if (measure(next) > available) break;
            fitted = next;
        }
        return fitted.TrimEnd();
    }

    internal static (string Name, string Amount, double NameEnd, double AmountStart) DrawLabel(Context ctx, string name, int amount, int row)
    {
        const double padding = 12, gap = 7;
        ctx.Save(); ctx.Translate(0, row * LabelPixels);
        ctx.SelectFontFace("monospace", FontSlant.Normal, FontWeight.Normal);
        ctx.SetFontSize(32);
        string quantity = amount.ToString(CultureInfo.InvariantCulture);
        double amountStart = LabelWidth - padding - ctx.TextExtents(quantity).XAdvance;
        ctx.SetFontSize(22);
        double crossWidth = ctx.TextExtents("x").XAdvance;
        double crossStart = amountStart - gap - crossWidth;
        ctx.SetFontSize(32);
        string prefix = Prefix(name, crossStart - gap - padding, text => ctx.TextExtents(text).XAdvance);
        double nameEnd = padding + ctx.TextExtents(prefix).XAdvance;
        ctx.SetSourceRGBA(.19, .16, .12, 1);
        ctx.MoveTo(padding, 34); ctx.ShowText(prefix);
        ctx.MoveTo(amountStart, 34); ctx.ShowText(quantity);
        ctx.SetFontSize(22); ctx.MoveTo(crossStart, 32); ctx.ShowText("x");
        ctx.Restore();
        return (prefix, quantity, nameEnd, amountStart);
    }

    internal static bool ReplacedElement(string? name) => name == "stock-paper-web" ||
        name?.StartsWith("stock-print-ink-", StringComparison.Ordinal) == true ||
        name?.StartsWith("stock-print-history-", StringComparison.Ordinal) == true;

    internal static MeshData Mesh(int quads)
    {
        var mesh = new MeshData(quads * 4, quads * 6, withNormals: false, withUv: true, withRgba: true, withFlags: true)
            { XyzStatic = false, UvStatic = false, FlagsStatic = false };
        for (int q = 0; q < quads; q++)
        {
            int v = q * 4;
            foreach (int i in new[] { v, v + 1, v + 2, v, v + 2, v + 3 }) mesh.AddIndex(i);
        }
        Pad(mesh, quads * 4);
        return mesh;
    }

    internal static void Pad(MeshData mesh, int vertices)
    {
        while (mesh.VerticesCount < vertices) mesh.AddVertexWithFlags(0, 0, 0, 0, 0, -1, 0);
    }

    // Each facet clips against a material interval. A label crossing a corner is
    // split at that corner, with matching UVs and no stretching or floating ink.
    internal static void Strip(MeshData mesh, Point a, Point b, double start, double end,
        double z0, double z1, double u0, double u1, double v0, double v1, double offset)
    {
        double length = b.S - a.S, nx = -(b.Y - a.Y) / length, ny = (b.X - a.X) / length;
        int flags = VertexFlags.PackNormal(nx, ny, 0);
        void Vertex(double at, double z, double u, double v)
        {
            double t = (at - a.S) / length;
            mesh.AddVertexWithFlags((float)((a.X + (b.X - a.X) * t + nx * offset) / 16),
                (float)((a.Y + (b.Y - a.Y) * t + ny * offset) / 16), (float)(z / 16), (float)u, (float)v, -1, flags);
        }
        Vertex(start, z0, u0, v0); Vertex(start, z1, u1, v0);
        Vertex(end, z1, u1, v1); Vertex(end, z0, u0, v1);
    }

    internal static void WriteGrain(MeshData mesh, TextureAtlasPosition texture, double phase)
    {
        mesh.VerticesCount = 0;
        double period = PneumaticStockkeeperState.GrainPeriod;
        for (int i = 1; i < Path.Length; i++)
        {
            var a = Path[i - 1]; var b = Path[i]; double from = a.S;
            while (from < b.S - 1e-9)
            {
                double material = from + PrintX - phase;
                double tile = Math.Floor((material + 1e-9) / period);
                double to = Math.Min(b.S, (tile + 1) * period - PrintX + phase);
                double v0 = (material - tile * period) / period, v1 = (to + PrintX - phase - tile * period) / period;
                // Native 32px parchment: one pixel per model unit, as on the cuboid.
                Strip(mesh, a, b, from, to, 6.6, 9.4,
                    texture.x1 + (texture.x2 - texture.x1) * 6.6 / period, texture.x1 + (texture.x2 - texture.x1) * 9.4 / period,
                    texture.y1 + (texture.y2 - texture.y1) * v0, texture.y1 + (texture.y2 - texture.y1) * v1, .006);
                from = to;
            }
        }
    }

    internal static void WriteLabel(MeshData mesh, double center, int row, double reveal = 1)
    {
        double start = center - LabelHeight / 2, end = center + LabelHeight / 2;
        for (int i = 1; i < Path.Length; i++)
        {
            var a = Path[i - 1]; var b = Path[i];
            double lo = Math.Max(a.S, start), hi = Math.Min(b.S, end);
            if (hi <= lo || reveal <= 0) continue;
            // Read from the take-up end (+X): the reader's left is +Z.
            // The carriage travels +Z, printing from the quantity end first.
            double v0 = (row + (lo - start) / LabelHeight) / (PneumaticStockkeeperState.HistoryLength + 1);
            double v1 = (row + (hi - start) / LabelHeight) / (PneumaticStockkeeperState.HistoryLength + 1);
            Strip(mesh, a, b, lo, hi, 6.6, 6.6 + Width * reveal, 1, 1 - reveal, v0, v1, .012);
        }
    }
}
