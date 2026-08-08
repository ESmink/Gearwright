using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>
/// Keeps a liquid pane stationary while wrapping its atlas texture across two moving UV segments.
/// </summary>
internal sealed class ScrollingLiquidSurface : IDisposable
{
    private const float MinimumSegment = 0.0001f;

    private readonly ICoreClientAPI capi;
    private readonly TextureAtlasPosition texture;
    private readonly float minX;
    private readonly float minY;
    private readonly float maxX;
    private readonly float maxY;
    private readonly float z;
    private readonly MeshData updateData;

    public ScrollingLiquidSurface(
        ICoreClientAPI capi,
        TextureAtlasPosition texture,
        float minX,
        float minY,
        float maxX,
        float maxY,
        float z)
    {
        this.capi = capi;
        this.texture = texture;
        this.minX = minX;
        this.minY = minY;
        this.maxX = maxX;
        this.maxY = maxY;
        this.z = z;

        MeshData initial = CreateInitialMesh();
        WriteSurface(initial.xyz, initial.Uv, 0, horizontal: true, reverse: false, minY, maxY);
        MeshRef = capi.Render.UploadMesh(initial);

        updateData = new MeshData(false)
        {
            xyz = new float[8 * 3],
            Uv = new float[8 * 2],
            VerticesCount = 8
        };
    }

    public MeshRef MeshRef { get; }
    public int TextureId => texture.atlasTextureId;

    public void Update(
        float phase,
        bool horizontal,
        bool reverse,
        float? minYOverride = null,
        float? maxYOverride = null)
    {
        WriteSurface(
            updateData.xyz,
            updateData.Uv,
            phase,
            horizontal,
            reverse,
            minYOverride ?? minY,
            maxYOverride ?? maxY);
        capi.Render.UpdateMesh(MeshRef, updateData);
    }

    private MeshData CreateInitialMesh()
    {
        MeshData mesh = new(8, 12, withNormals: false, withUv: true, withRgba: true, withFlags: true)
        {
            XyzStatic = false,
            UvStatic = false,
            TextureIds = new[] { texture.atlasTextureId }
        };
        mesh.SetVerticesCount(8);
        mesh.SetIndicesCount(12);

        int[] indices = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        Array.Copy(indices, mesh.Indices, indices.Length);
        for (int vertex = 0; vertex < 8; vertex++)
        {
            int color = vertex * 4;
            mesh.Rgba[color] = 255;
            mesh.Rgba[color + 1] = 255;
            mesh.Rgba[color + 2] = 255;
            mesh.Rgba[color + 3] = 255;
            mesh.Flags[vertex] = BlockFacing.NORTH.NormalPackedFlags;
        }

        mesh.TextureIndices[0] = 0;
        mesh.TextureIndices[1] = 0;
        mesh.TextureIndicesCount = 2;
        return mesh;
    }

    private void WriteSurface(
        float[] xyz,
        float[] uv,
        float phase,
        bool horizontal,
        bool reverse,
        float surfaceMinY,
        float surfaceMaxY)
    {
        float wrapped = phase - MathF.Floor(phase);
        if (reverse && wrapped > 0) wrapped = 1 - wrapped;
        wrapped = Math.Clamp(wrapped, MinimumSegment, 1 - MinimumSegment);

        float textureWidth = texture.x2 - texture.x1;
        float textureHeight = texture.y2 - texture.y1;
        if (horizontal)
        {
            float seam = maxX - (maxX - minX) * wrapped;
            WriteNorthQuad(
                xyz, uv, 0, minX, surfaceMinY, seam, surfaceMaxY,
                texture.x1 + textureWidth * wrapped, texture.y1,
                texture.x2, texture.y2);
            WriteNorthQuad(
                xyz, uv, 4, seam, surfaceMinY, maxX, surfaceMaxY,
                texture.x1, texture.y1,
                texture.x1 + textureWidth * wrapped, texture.y2);
            return;
        }

        float verticalSeam = surfaceMaxY - (surfaceMaxY - surfaceMinY) * wrapped;
        WriteNorthQuad(
            xyz, uv, 0, minX, surfaceMinY, maxX, verticalSeam,
            texture.x1, texture.y1 + textureHeight * wrapped,
            texture.x2, texture.y2);
        WriteNorthQuad(
            xyz, uv, 4, minX, verticalSeam, maxX, surfaceMaxY,
            texture.x1, texture.y1,
            texture.x2, texture.y1 + textureHeight * wrapped);
    }

    private void WriteNorthQuad(
        float[] xyz,
        float[] uv,
        int vertex,
        float x1,
        float y1,
        float x2,
        float y2,
        float u1,
        float v1,
        float u2,
        float v2)
    {
        WriteVertex(xyz, uv, vertex, x1, y1, z, u1, v2);
        WriteVertex(xyz, uv, vertex + 1, x1, y2, z, u1, v1);
        WriteVertex(xyz, uv, vertex + 2, x2, y2, z, u2, v1);
        WriteVertex(xyz, uv, vertex + 3, x2, y1, z, u2, v2);
    }

    private static void WriteVertex(
        float[] xyz,
        float[] uv,
        int vertex,
        float x,
        float y,
        float z,
        float u,
        float v)
    {
        int xyzIndex = vertex * 3;
        xyz[xyzIndex] = x;
        xyz[xyzIndex + 1] = y;
        xyz[xyzIndex + 2] = z;
        int uvIndex = vertex * 2;
        uv[uvIndex] = u;
        uv[uvIndex + 1] = v;
    }

    public void Dispose() => MeshRef.Dispose();
}
