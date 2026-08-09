using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Gearwright.Hydraulics;

public sealed class BlockEntityPassiveFluidPump : BlockEntityHydraulicPump
{
    private BlockFacing facing = BlockFacing.NORTH;
    private PassiveFluidPumpRenderer? renderer;

    public BlockFacing Facing => facing;
    public BlockFacing IntakeFace => FindIntakeFace();

    public override bool CanConnect(BlockFacing face) => face == facing;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is ICoreClientAPI capi)
        {
            renderer = new PassiveFluidPumpRenderer(this, capi);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-passive-fluid-pump");
        }
    }

    public void SetFacing(BlockFacing requestedFacing)
    {
        if (!requestedFacing.IsHorizontal || !CanWriteState) return;
        facing = requestedFacing;
        MarkHydraulicsDirty(true);
        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            Api?.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        }
        Api?.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
    }

    public override PumpOffer GetOffer()
        => new(this, new AssetLocation(HydraulicCodes.FreshWater), 0, "deprecated");

    protected override bool IsSourceFace(BlockFacing face)
    {
        SourceConnection? source = FindSource();
        return source?.Face == face;
    }

    private SourceConnection? FindSource()
    {
        if (Api == null) return null;
        return TrySource(FindIntakeFace());
    }

    private BlockFacing FindIntakeFace()
    {
        if (Api == null) return facing.Opposite;
        if (HasTank(facing.Opposite)) return facing.Opposite;
        if (HasTank(BlockFacing.UP)) return BlockFacing.UP;
        return facing.Opposite;
    }

    private bool HasTank(BlockFacing face)
    {
        Block block = Api.World.BlockAccessor.GetBlock(Pos.AddCopy(face));
        return block is ILiquidSource && block is ILiquidInterface;
    }

    private SourceConnection? TrySource(BlockFacing face)
    {
        BlockPos sourcePos = Pos.AddCopy(face);
        Block block = Api.World.BlockAccessor.GetBlock(sourcePos);
        if (block is not ILiquidSource source || block is not ILiquidInterface liquid) return null;
        if (Api.World.BlockAccessor.GetBlockEntity(sourcePos) is BlockEntityBarrel { Sealed: true }) return null;
        ItemStack? content = liquid.GetContent(sourcePos);
        if (content == null || content.StackSize <= 0) return null;
        return new SourceConnection(face, sourcePos, source, liquid, content, face.IsHorizontal);
    }

    protected override void ReadPumpState(ITreeAttribute state, IWorldAccessor world)
    {
        BlockFacing? storedFacing = state.HasAttribute("facing")
            ? BlockFacing.FromCode(state.GetString("facing", "north"))
            : null;
        facing = storedFacing?.IsHorizontal == true ? storedFacing : BlockFacing.NORTH;
    }

    protected override void WritePumpState(ITreeAttribute state)
    {
        state.SetString("facing", facing.Code);
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        MeshData body = HydraulicPipeMesh.Tesselate(
            this, tessThreadTesselator, "gearwright:shapes/block/passive-fluid-pump.json");
        HydraulicPipeMesh.RotateNorthPart(body, facing);
        mesher.AddMeshData(body, 1);
        AddOutletMesh(facing, mesher, tessThreadTesselator);
        AddIntakeMesh(IntakeFace, mesher, tessThreadTesselator);
        return true;
    }

    public override void OnBlockRemoved()
    {
        StopRenderer();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopRenderer();
        base.OnBlockUnloaded();
    }

    private void AddOutletMesh(BlockFacing face, ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        MeshData outlet = HydraulicPipeMesh.Tesselate(
            this, tesselator, "gearwright:shapes/block/passive-fluid-pump-outlet.json");
        HydraulicPipeMesh.RotateNorthPart(outlet, face);
        mesher.AddMeshData(outlet, 1);
    }

    private void AddIntakeMesh(BlockFacing face, ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        MeshData intake = HydraulicPipeMesh.Tesselate(
            this, tesselator, "gearwright:shapes/block/passive-fluid-pump-intake.json");
        HydraulicPipeMesh.RotateSouthPart(intake, face);
        mesher.AddMeshData(intake, 1);
    }

    public void OnTankNeighbourChanged()
    {
        MarkHydraulicsDirty(true);
        Api?.World.BlockAccessor.MarkBlockDirty(Pos);
    }

    private void StopRenderer()
    {
        if (renderer == null || Api is not ICoreClientAPI capi) return;
        capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
        renderer.Dispose();
        renderer = null;
    }

    private readonly record struct SourceConnection(
        BlockFacing Face,
        BlockPos Position,
        ILiquidSource Source,
        ILiquidInterface Interface,
        ItemStack Content,
        bool Horizontal);
}
