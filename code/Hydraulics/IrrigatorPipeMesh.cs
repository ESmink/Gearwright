using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

internal static class IrrigatorPipeMesh
{
    public static bool AddStaticMeshes(
        BlockEntityIrrigatorPipe pipe,
        ITerrainMeshPool mesher,
        ITesselatorAPI tesselator)
    {
        MeshData combined = HydraulicPipeMesh.Tesselate(
            pipe, tesselator, "gearwright:shapes/block/irrigator-pipe-body.json");

        AddEnd(pipe, tesselator, combined, BlockFacing.NORTH, pipe.NegativeFace);
        AddEnd(pipe, tesselator, combined, BlockFacing.SOUTH, pipe.PositiveFace);

        if ((pipe.SupportMask & BlockEntityIrrigatorPipe.NegativeSupport) != 0)
            AddSupport(pipe, tesselator, combined, BlockFacing.NORTH);
        if ((pipe.SupportMask & BlockEntityIrrigatorPipe.PositiveSupport) != 0)
            AddSupport(pipe, tesselator, combined, BlockFacing.SOUTH);

        if (pipe.AlongX)
            combined.Rotate(new Vec3f(.5f, .5f, .5f), 0, GameMath.PIHALF, 0);
        mesher.AddMeshData(combined, 1);
        return true;
    }

    private static void AddEnd(
        BlockEntityIrrigatorPipe pipe,
        ITesselatorAPI tesselator,
        MeshData combined,
        BlockFacing authoredFace,
        BlockFacing worldFace)
    {
        string shape = pipe.IsConnected(worldFace)
            ? "gearwright:shapes/block/irrigator-pipe-connection.json"
            : "gearwright:shapes/block/irrigator-pipe-endpoint.json";
        MeshData part = HydraulicPipeMesh.Tesselate(pipe, tesselator, shape);
        HydraulicPipeMesh.RotateNorthPart(part, authoredFace);
        combined.AddMeshData(part);
    }

    private static void AddSupport(
        BlockEntityIrrigatorPipe pipe,
        ITesselatorAPI tesselator,
        MeshData combined,
        BlockFacing authoredFace)
    {
        // Use the same ordinary block texture resolution as the working
        // inventory model. The Irrigator block's wood alias is fixed to oak.
        MeshData support = HydraulicPipeMesh.Tesselate(
            pipe, tesselator,
            "gearwright:shapes/block/irrigator-pipe-support.json");
        HydraulicPipeMesh.RotateNorthPart(support, authoredFace);
        combined.AddMeshData(support);
    }
}
