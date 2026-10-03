using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Gearwright.Rendering;

/// <summary>Adapts the approved glass shapes to the engine's actual transparency passes.</summary>
internal static class InspectionGlass
{
    internal static Shape TerrainShape(Shape source)
    {
        // Historical shape definitions label glass with pass 1, which the
        // engine calls OpaqueNoCull. Work on a clone: loaded shapes are shared
        // with inventory meshes and other tessellation threads.
        Shape shape = source.Clone();
        var glassKeys = source.Textures.Where(pair =>
            pair.Value.Domain == "gearwright" && pair.Value.Path == "block/inspection-glass")
            .Select(pair => pair.Key.TrimStart('#')).ToHashSet();
        void Visit(ShapeElement element)
        {
            var faces = element.FacesResolved?.Where(face => face != null && face.Enabled).ToArray();
            if (faces is { Length: > 0 } && faces.All(face => glassKeys.Contains(face.Texture.TrimStart('#'))))
                element.RenderPass = (short)EnumChunkRenderPass.Transparent;
            foreach (var child in element.Children ?? Array.Empty<ShapeElement>()) Visit(child);
        }
        foreach (var element in shape.Elements) Visit(element);
        return shape;
    }

    internal static void PrepareAnimatedMesh(MeshData mesh)
    {
        // The native OIT shader accepts an animation matrix. Glass bones are
        // already posed by their model matrix, so every vertex uses identity
        // joint zero and has no damage effect (attributes 4 and 5).
        mesh.CustomFloats = new CustomMeshDataPartFloat
        {
            Values = new float[mesh.VerticesCount], Count = mesh.VerticesCount,
            InterleaveSizes = new[] { 1 }, InterleaveOffsets = new[] { 0 }, InterleaveStride = sizeof(float)
        };
        mesh.CustomInts = new CustomMeshDataPartInt
        {
            Values = new int[mesh.VerticesCount], Count = mesh.VerticesCount,
            InterleaveSizes = new[] { 1 }, InterleaveOffsets = new[] { 0 }, InterleaveStride = sizeof(int),
            Conversion = DataConversion.Integer
        };
    }
}
