using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

/// <summary>Client mesh composition for pipe connections and face add-ons.</summary>
internal static class HydraulicPipeMesh
{
    public static bool AddStaticMeshes(
        BlockEntityFluidPipe pipe,
        ITerrainMeshPool mesher,
        ITesselatorAPI tesselator)
    {
        MeshData combined = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-center.json");
        foreach (BlockFacing face in BlockFacing.ALLFACES)
        {
            MeshData? part = null;
            if (pipe.IsPortEnabled(face))
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-arm.json");
            }
            else if (pipe.GetAddon(face) == HydraulicFaceAddon.GlassWindow)
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-window.json");
            }
            else if (pipe.GetAddon(face) == HydraulicFaceAddon.Sprinkler)
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/sprinkler-body.json");
            }
            else if (pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle)
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-intake.json");
            }
            else if (pipe.GetAddon(face) == HydraulicFaceAddon.CopperFlange)
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-arm.json");
                MeshData flange = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-flange.json");
                part.AddMeshData(flange);
            }
            else
            {
                part = Tesselate(pipe, tesselator, "gearwright:shapes/block/fluid-pipe-cap.json");
            }

            if (part == null) continue;
            if (pipe.GetAddon(face) == HydraulicFaceAddon.PipeNozzle) RotateSouthPart(part, face);
            else if (pipe.GetAddon(face) != HydraulicFaceAddon.Sprinkler) RotateNorthPart(part, face);
            combined.AddMeshData(part);
        }

        mesher.AddMeshData(combined, 1);
        return true;
    }

    internal static MeshData Tesselate(BlockEntityHydraulicNode node, ITesselatorAPI tesselator, string shapeCode)
    {
        Shape shape = Shape.TryGet(node.Api, shapeCode);
        tesselator.TesselateShape(node.Block, shape, out MeshData mesh, new Vec3f(), null, null);
        return mesh;
    }

    internal static void RotateNorthPart(MeshData mesh, BlockFacing face)
    {
        Vec3f center = new(0.5f, 0.5f, 0.5f);
        if (face == BlockFacing.EAST) mesh.Rotate(center, 0, -GameMath.PIHALF, 0);
        else if (face == BlockFacing.SOUTH) mesh.Rotate(center, 0, GameMath.PI, 0);
        else if (face == BlockFacing.WEST) mesh.Rotate(center, 0, GameMath.PIHALF, 0);
        else if (face == BlockFacing.UP) mesh.Rotate(center, GameMath.PIHALF, 0, 0);
        else if (face == BlockFacing.DOWN) mesh.Rotate(center, -GameMath.PIHALF, 0, 0);
    }

    internal static void RotateSouthPart(MeshData mesh, BlockFacing face) =>
        RotateNorthPart(mesh, face.Opposite);
}
