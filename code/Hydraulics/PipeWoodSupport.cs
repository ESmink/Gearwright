using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Local, derived wooden support presentation; no structural network.</summary>
internal static class PipeWoodSupport
{
    internal static void AddMeshes(BlockEntityFluidPipe pipe, ITesselatorAPI tess, MeshData combined)
    {
        IBlockAccessor accessor = pipe.Api.World.BlockAccessor;
        void Add(string name, BlockFacing? face = null)
        {
            MeshData mesh = HydraulicPipeMesh.Tesselate(pipe, tess, "gearwright:shapes/block/pipe-support-" + name + ".json");
            if (face != null) HydraulicPipeMesh.RotateNorthPart(mesh, face);
            combined.AddMeshData(mesh);
        }
        Add("frame");
        bool anyEndpoint = false;
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            bool endpoint = pipe.HasSupportEndpoint(face);
            anyEndpoint |= endpoint;
            bool wide = pipe.GetAddon(face) is HydraulicFaceAddon.PipeNozzle or HydraulicFaceAddon.Sprinkler;
            if (endpoint || face != BlockFacing.UP) Add(wide ? "nozzle-endpoint" : endpoint ? "endpoint" : "corners", face);
            if (pipe.HasWoodInsulation) Add(pipe.GetAddon(face) == HydraulicFaceAddon.Sprinkler
                ? "lining-sprinkler" : wide ? "lining-nozzle" : endpoint ? "lining-port" : "lining", face);
        }
        if (!pipe.HasWoodInsulation && pipe.HasSupportPlatform(accessor)) Add(pipe.HasSupportEndpoint(BlockFacing.UP) ? "top-opening" : "top");
        if (!anyEndpoint && !pipe.HasSprinkler) Add("seat");
    }

    internal static void AddBoxes(BlockEntityFluidPipe pipe, IBlockAccessor accessor, List<Cuboidf> boxes, bool collision)
    {
        foreach (float a in new[] { 0f, 14.5f })
        foreach (float b in new[] { 0f, 14.5f })
        {
            boxes.Add(Box(a, 0, b, a + 1.5f, 16, b + 1.5f));
            boxes.Add(Box(1.5f, a, b, 14.5f, a + 1.5f, b + 1.5f));
            boxes.Add(Box(a, b, 1.5f, a + 1.5f, b + 1.5f, 14.5f));
        }
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            if (pipe.HasSupportEndpoint(face))
            {
                float inner = pipe.GetAddon(face) is HydraulicFaceAddon.PipeNozzle or HydraulicFaceAddon.Sprinkler ? 4.5f : 5.5f;
                float outer = inner - 1;
                AddFaceBox(boxes, face, outer, outer, 0, 16 - outer, inner, 1);
                AddFaceBox(boxes, face, outer, 16 - inner, 0, 16 - outer, 16 - outer, 1);
                AddFaceBox(boxes, face, outer, inner, 0, inner, 16 - inner, 1);
                AddFaceBox(boxes, face, 16 - inner, inner, 0, 16 - outer, 16 - inner, 1);
            }
        }
        if (!pipe.HasSupportPlatform(accessor)) return;
        if (collision) boxes.Add(Box(0, 14.5f, 0, 16, 16, 16));
        else
        {
            boxes.Add(Box(1.5f, 14.5f, 1.5f, 14.5f, 16, 3.5f));
            if (!pipe.HasSupportEndpoint(BlockFacing.UP)) boxes.Add(Box(1.5f, 14.5f, 6.75f, 14.5f, 16, 9.25f));
            boxes.Add(Box(1.5f, 14.5f, 12.5f, 14.5f, 16, 14.5f));
        }
    }

    private static void AddFaceBox(List<Cuboidf> boxes, BlockFacing face, float x1, float y1, float z1, float x2, float y2, float z2)
    {
        if (face == BlockFacing.NORTH) boxes.Add(Box(x1, y1, z1, x2, y2, z2));
        else if (face == BlockFacing.SOUTH) boxes.Add(Box(16 - x2, y1, 16 - z2, 16 - x1, y2, 16 - z1));
        else if (face == BlockFacing.EAST) boxes.Add(Box(16 - z2, y1, x1, 16 - z1, y2, x2));
        else if (face == BlockFacing.WEST) boxes.Add(Box(z1, y1, 16 - x2, z2, y2, 16 - x1));
        else if (face == BlockFacing.UP) boxes.Add(Box(x1, 16 - z2, y1, x2, 16 - z1, y2));
        else boxes.Add(Box(x1, z1, 16 - y2, x2, z2, 16 - y1));
    }

    private static Cuboidf Box(float x1, float y1, float z1, float x2, float y2, float z2) =>
        new(x1 / 16, y1 / 16, z1 / 16, x2 / 16, y2 / 16, z2 / 16);
}
