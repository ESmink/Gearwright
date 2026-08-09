using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>A fixed-topology mesh with a clipped liquid surface and atlas-safe scrolling UVs.</summary>
internal sealed class PipeContentMesh : IDisposable
{
    private const float MinimumSegment = 0.0001f;
    private static readonly BlockFacing[] FaceNormals =
    {
        BlockFacing.NORTH, BlockFacing.SOUTH, BlockFacing.WEST,
        BlockFacing.EAST, BlockFacing.DOWN, BlockFacing.UP
    };

    private readonly ICoreClientAPI capi;
    private readonly TextureAtlasPosition texture;
    private readonly Segment[] segments;
    private readonly MeshData mesh;
    private readonly MeshData updateData;
    private double displayedFill = -1;
    private bool displayedGas;
    private float displayedPhase = -1;
    private int displayedFlowDirection = -1;

    public PipeContentMesh(
        ICoreClientAPI capi,
        TextureAtlasPosition texture,
        BlockEntityFluidPipe pipe)
    {
        this.capi = capi;
        this.texture = texture;
        segments = BuildSegments(pipe).ToArray();
        int quads = segments.Length * FaceNormals.Length * 2;
        mesh = new MeshData(quads * 4, quads * 6, withNormals: false, withUv: true, withRgba: true, withFlags: true)
        {
            XyzStatic = false,
            UvStatic = false,
            TextureIds = new[] { texture.atlasTextureId }
        };
        mesh.SetVerticesCount(quads * 4);
        mesh.SetIndicesCount(quads * 6);
        mesh.TextureIndicesCount = quads;
        PrepareStaticMesh();
        WriteGeometry(1, gas: true, 0, null);
        MeshRef = capi.Render.UploadMesh(mesh);
        updateData = new MeshData(false)
        {
            xyz = new float[mesh.VerticesCount * 3],
            Uv = new float[mesh.VerticesCount * 2],
            VerticesCount = mesh.VerticesCount
        };
    }

    public MeshRef MeshRef { get; }
    public int TextureId => texture.atlasTextureId;

    public void Update(
        double fillFraction,
        bool gas,
        float texturePhase,
        BlockFacing? flowDirection)
    {
        double fill = Math.Clamp(double.IsFinite(fillFraction) ? fillFraction : 0, 0, 1);
        float phase = texturePhase - MathF.Floor(texturePhase);
        int direction = flowDirection?.Index ?? -1;
        if (Math.Abs(fill - displayedFill) < 0.001 &&
            gas == displayedGas &&
            Math.Abs(phase - displayedPhase) < 0.0001f &&
            direction == displayedFlowDirection) return;
        displayedFill = fill;
        displayedGas = gas;
        displayedPhase = phase;
        displayedFlowDirection = direction;
        WriteGeometry(fill, gas, phase, flowDirection);
        Array.Copy(mesh.xyz, updateData.xyz, mesh.xyz.Length);
        Array.Copy(mesh.Uv, updateData.Uv, mesh.Uv.Length);
        capi.Render.UpdateMesh(MeshRef, updateData);
    }

    public static int TopologyMask(BlockEntityFluidPipe pipe)
    {
        int mask = 0;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.IsPortEnabled(face)) mask |= 1 << face.Index;
        }
        return mask;
    }

    private static List<Segment> BuildSegments(BlockEntityFluidPipe pipe)
    {
        const float low = 6.5f / 16f;
        const float high = 9.5f / 16f;
        int centerHidden = 0;
        if (pipe.IsPortEnabled(BlockFacing.NORTH)) centerHidden |= 1 << 0;
        if (pipe.IsPortEnabled(BlockFacing.SOUTH)) centerHidden |= 1 << 1;
        if (pipe.IsPortEnabled(BlockFacing.WEST)) centerHidden |= 1 << 2;
        if (pipe.IsPortEnabled(BlockFacing.EAST)) centerHidden |= 1 << 3;
        if (pipe.IsPortEnabled(BlockFacing.DOWN)) centerHidden |= 1 << 4;
        if (pipe.IsPortEnabled(BlockFacing.UP)) centerHidden |= 1 << 5;
        List<Segment> result = new() { new Segment(low, low, low, high, high, high, centerHidden) };
        if (pipe.IsPortEnabled(BlockFacing.NORTH)) result.Add(new Segment(low, low, 0, high, high, low, 1 << 1));
        if (pipe.IsPortEnabled(BlockFacing.SOUTH)) result.Add(new Segment(low, low, high, high, high, 1, 1 << 0));
        if (pipe.IsPortEnabled(BlockFacing.WEST)) result.Add(new Segment(0, low, low, low, high, high, 1 << 3));
        if (pipe.IsPortEnabled(BlockFacing.EAST)) result.Add(new Segment(high, low, low, 1, high, high, 1 << 2));
        if (pipe.IsPortEnabled(BlockFacing.DOWN)) result.Add(new Segment(low, 0, low, high, low, high, 1 << 5));
        if (pipe.IsPortEnabled(BlockFacing.UP)) result.Add(new Segment(low, high, low, high, 1, high, 1 << 4));
        return result;
    }

    private void PrepareStaticMesh()
    {
        int vertex = 0;
        int index = 0;
        int quadIndex = 0;
        foreach (Segment _ in segments)
        foreach (BlockFacing normal in FaceNormals)
        for (int half = 0; half < 2; half++)
        {
            mesh.Indices[index++] = vertex;
            mesh.Indices[index++] = vertex + 1;
            mesh.Indices[index++] = vertex + 2;
            mesh.Indices[index++] = vertex;
            mesh.Indices[index++] = vertex + 2;
            mesh.Indices[index++] = vertex + 3;
            mesh.TextureIndices[quadIndex++] = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                int current = vertex + corner;
                mesh.Flags[current] = normal.NormalPackedFlags;
                int color = current * 4;
                mesh.Rgba[color] = 255;
                mesh.Rgba[color + 1] = 255;
                mesh.Rgba[color + 2] = 255;
                mesh.Rgba[color + 3] = 255;
            }
            vertex += 4;
        }
    }

    private void WriteGeometry(double fill, bool gas, float phase, BlockFacing? flowDirection)
    {
        float surface = gas ? 1 : FindLiquidSurface(fill);
        int vertex = 0;
        foreach (Segment segment in segments)
        {
            if (!gas && surface <= segment.Y1 + 0.000001f)
            {
                WriteBox(vertex, segment.X1, segment.Y1, segment.Z1,
                    segment.X2, segment.Y1, segment.Z2, 0x3f, phase, flowDirection);
                vertex += 48;
                continue;
            }
            float clippedTop = gas ? segment.Y2 : Math.Clamp(surface, segment.Y1, segment.Y2);
            WriteBox(vertex, segment.X1, segment.Y1, segment.Z1,
                segment.X2, clippedTop, segment.Z2, segment.HiddenFaces, phase, flowDirection);
            vertex += 48;
        }
    }

    private float FindLiquidSurface(double fill)
    {
        float minimum = 1;
        float maximum = 0;
        double totalVolume = 0;
        foreach (Segment segment in segments)
        {
            minimum = Math.Min(minimum, segment.Y1);
            maximum = Math.Max(maximum, segment.Y2);
            totalVolume += segment.Volume;
        }
        double target = totalVolume * fill;
        for (int iteration = 0; iteration < 20; iteration++)
        {
            float middle = (minimum + maximum) * 0.5f;
            double below = 0;
            foreach (Segment segment in segments) below += segment.VolumeBelow(middle);
            if (below < target) minimum = middle;
            else maximum = middle;
        }
        return (minimum + maximum) * 0.5f;
    }

    private void WriteBox(
        int vertex,
        float x1, float y1, float z1,
        float x2, float y2, float z2,
        int hiddenFaces,
        float phase,
        BlockFacing? flowDirection)
    {
        WriteFace(hiddenFaces, 0, vertex,
            new Point(x1, y1, z1), new Point(x1, y2, z1),
            new Point(x2, y2, z1), new Point(x2, y1, z1), phase, flowDirection);
        WriteFace(hiddenFaces, 1, vertex + 8,
            new Point(x2, y1, z2), new Point(x2, y2, z2),
            new Point(x1, y2, z2), new Point(x1, y1, z2), phase, flowDirection);
        WriteFace(hiddenFaces, 2, vertex + 16,
            new Point(x1, y1, z2), new Point(x1, y2, z2),
            new Point(x1, y2, z1), new Point(x1, y1, z1), phase, flowDirection);
        WriteFace(hiddenFaces, 3, vertex + 24,
            new Point(x2, y1, z1), new Point(x2, y2, z1),
            new Point(x2, y2, z2), new Point(x2, y1, z2), phase, flowDirection);
        WriteFace(hiddenFaces, 4, vertex + 32,
            new Point(x1, y1, z2), new Point(x1, y1, z1),
            new Point(x2, y1, z1), new Point(x2, y1, z2), phase, flowDirection);
        WriteFace(hiddenFaces, 5, vertex + 40,
            new Point(x1, y2, z1), new Point(x1, y2, z2),
            new Point(x2, y2, z2), new Point(x2, y2, z1), phase, flowDirection);
    }

    private void WriteFace(
        int hiddenFaces,
        int face,
        int vertex,
        Point a,
        Point b,
        Point c,
        Point d,
        float phase,
        BlockFacing? flowDirection)
    {
        if ((hiddenFaces & (1 << face)) != 0)
        {
            WriteQuad(vertex, a, a, a, a);
            WriteQuad(vertex + 4, a, a, a, a);
            WriteStaticUv(vertex);
            return;
        }

        if (!TryOrientAlongFlow(
                a, b, c, d, FaceNormals[face], flowDirection,
                out Point startA, out Point startB, out Point endC, out Point endD))
        {
            WriteQuad(vertex, a, b, Midpoint(b, c), Midpoint(a, d));
            WriteQuad(vertex + 4, Midpoint(a, d), Midpoint(b, c), c, d);
            WriteStaticUv(vertex);
            return;
        }

        float wrapped = phase - MathF.Floor(phase);
        if (PositiveAxis(flowDirection!) && wrapped > 0) wrapped = 1 - wrapped;
        wrapped = Math.Clamp(wrapped, MinimumSegment, 1 - MinimumSegment);
        float seam = 1 - wrapped;
        Point seamA = Lerp(startA, endD, seam);
        Point seamB = Lerp(startB, endC, seam);
        WriteQuad(vertex, startA, startB, seamB, seamA);
        WriteQuad(vertex + 4, seamA, seamB, endC, endD);

        float width = texture.x2 - texture.x1;
        WriteUvQuad(vertex, texture.x1 + width * wrapped, texture.x2);
        WriteUvQuad(vertex + 4, texture.x1, texture.x1 + width * wrapped);
    }

    private static bool TryOrientAlongFlow(
        Point a,
        Point b,
        Point c,
        Point d,
        BlockFacing normal,
        BlockFacing? flowDirection,
        out Point startA,
        out Point startB,
        out Point endC,
        out Point endD)
    {
        startA = a;
        startB = b;
        endC = c;
        endD = d;
        if (flowDirection == null ||
            normal.Normali.X * flowDirection.Normali.X +
            normal.Normali.Y * flowDirection.Normali.Y +
            normal.Normali.Z * flowDirection.Normali.Z != 0) return false;

        float aAxis = AxisCoordinate(a, flowDirection);
        float bAxis = AxisCoordinate(b, flowDirection);
        float dAxis = AxisCoordinate(d, flowDirection);
        if (Math.Abs(dAxis - aAxis) > 0.000001f)
        {
            if (aAxis <= dAxis) return true;
            startA = c;
            startB = d;
            endC = a;
            endD = b;
            return true;
        }
        if (Math.Abs(bAxis - aAxis) <= 0.000001f) return false;
        if (aAxis <= bAxis)
        {
            startA = d;
            startB = a;
            endC = b;
            endD = c;
        }
        else
        {
            startA = b;
            startB = c;
            endC = d;
            endD = a;
        }
        return true;
    }

    private static float AxisCoordinate(Point point, BlockFacing direction)
    {
        if (direction.Normali.X != 0) return point.X;
        if (direction.Normali.Y != 0) return point.Y;
        return point.Z;
    }

    private static bool PositiveAxis(BlockFacing direction) =>
        direction.Normali.X + direction.Normali.Y + direction.Normali.Z > 0;

    private void WriteStaticUv(int vertex)
    {
        float middle = (texture.x1 + texture.x2) * 0.5f;
        WriteUvQuad(vertex, texture.x1, middle);
        WriteUvQuad(vertex + 4, middle, texture.x2);
    }

    private void WriteUvQuad(int vertex, float u1, float u2)
    {
        WriteUvVertex(vertex, u1, texture.y2);
        WriteUvVertex(vertex + 1, u1, texture.y1);
        WriteUvVertex(vertex + 2, u2, texture.y1);
        WriteUvVertex(vertex + 3, u2, texture.y2);
    }

    private void WriteUvVertex(int vertex, float u, float v)
    {
        mesh.Uv[vertex * 2] = u;
        mesh.Uv[vertex * 2 + 1] = v;
    }

    private void WriteQuad(int vertex, Point a, Point b, Point c, Point d)
    {
        WriteVertex(vertex, a);
        WriteVertex(vertex + 1, b);
        WriteVertex(vertex + 2, c);
        WriteVertex(vertex + 3, d);
    }

    private void WriteVertex(int vertex, Point point)
    {
        int index = vertex * 3;
        mesh.xyz[index] = point.X;
        mesh.xyz[index + 1] = point.Y;
        mesh.xyz[index + 2] = point.Z;
    }

    private static Point Midpoint(Point a, Point b) => Lerp(a, b, 0.5f);

    private static Point Lerp(Point a, Point b, float amount) => new(
        a.X + (b.X - a.X) * amount,
        a.Y + (b.Y - a.Y) * amount,
        a.Z + (b.Z - a.Z) * amount);

    public void Dispose() => MeshRef.Dispose();

    private readonly record struct Point(float X, float Y, float Z);

    private readonly record struct Segment(
        float X1, float Y1, float Z1,
        float X2, float Y2, float Z2,
        int HiddenFaces)
    {
        public double Volume => (X2 - X1) * (Y2 - Y1) * (Z2 - Z1);
        public double VolumeBelow(float surface) =>
            (X2 - X1) * Math.Clamp(surface - Y1, 0, Y2 - Y1) * (Z2 - Z1);
    }
}
