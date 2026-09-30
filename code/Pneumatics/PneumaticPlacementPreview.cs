using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

/// <summary>Physical arrowheads show the same port choice as server placement.</summary>
public sealed class PneumaticPlacementPreview : ModSystem, IRenderer
{
    private ICoreClientAPI? api;
    public double RenderOrder => 1;
    public int RenderRange => 16;
    public override void StartClientSide(ICoreClientAPI api)
    { this.api = api; api.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "gearwright-pneumatic-preview"); }
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (api == null) return;
        var selection = api.World.Player.CurrentBlockSelection;
        var held = api.World.Player.InventoryManager.ActiveHotbarSlot.Itemstack;
        BlockFacing input, output;
        BlockFacing? inventory = null;
        bool sender = false;
        BlockPos pos;
        bool accumulator = false;
        if (held?.Block is BlockPneumaticTransport block)
        {
            accumulator = block.Code.Path == "pneumatic-accumulator";
            var plan = block.Pending(api.World, api.World.Player);
            if (plan != null)
            {
                pos = plan.Selection.Position; input = plan.Input; output = plan.Output;
                inventory = PneumaticPlacement.InventoryDirection(plan, api.World.Player);
                Frame(pos);
            }
            else
            {
                if (selection == null) return;
                var target = PneumaticPlacement.Target(api.World, block, selection);
                pos = target.Position;
                if (block.Code.Path == "pneumatic-router")
                {
                    var mount = target.Face.Opposite;
                    RouterPreview(pos, new PneumaticRouterState { Mount = mount, Forward = PneumaticPlacement.Aim(api.World.Player, mount.Axis) });
                    return;
                }
                (input, output) = block.PlacementFaces(api.World, api.World.Player, target);
                if (block.IsEndpoint) inventory = PneumaticPlacement.DefaultInventory(output);
            }
            sender = block.Code.Path == "pneumatic-sender";
        }
        else if (selection != null && held?.Collectible.Tool == EnumTool.Wrench && api.World.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityPneumaticTransport host)
        {
            if (host.Kind == PneumaticLineKind.Router) { RouterPreview(selection.Position, host.Router, host); return; }
            pos = selection.Position; input = host.State.Input; output = host.State.Output;
            if (host.Kind != PneumaticLineKind.Tube) inventory = host.State.InventoryFace;
            sender = host.Kind == PneumaticLineKind.Sender;
        }
        else if (selection != null && held?.Collectible.Tool == EnumTool.Wrench && api.World.BlockAccessor.GetBlockEntity(selection.Position) is BlockEntityPneumaticAirIntake intake)
        { pos = selection.Position; output = intake.Outlet; input = output.Opposite; accumulator = true; }
        else return;
        if (accumulator)
        {
            foreach (var face in BlockFacing.HORIZONTALS)
                if (face != output) Arrow(pos, face.Normali, true);
        }
        else Arrow(pos, input.Normali, true);
        Arrow(pos, output.Normali, false);
        if (inventory != null) Arrow(pos, inventory.Normali, sender, unchecked((int)0xffe6a6ef));
    }
    private void RouterPreview(BlockPos pos, PneumaticRouterState router, BlockEntityPneumaticTransport? host = null)
    {
        Frame(pos);
        var up = router.Mount.Opposite.Normali;
        for (int port = 1; port <= 4; port++)
        {
            var normal = router.Face(port).Normali;
            var across = new Vec3i(normal.Y * up.Z - normal.Z * up.Y, normal.Z * up.X - normal.X * up.Z,
                normal.X * up.Y - normal.Y * up.X);
            for (int tick = 0; tick < port; tick++)
            {
                float offset = (tick - (port - 1) * .5f) * .045f;
                float x = .5f + normal.X * .49f + across.X * offset;
                float y = .5f + normal.Y * .49f + across.Y * offset;
                float z = .5f + normal.Z * .49f + across.Z * offset;
                api!.Render.RenderLine(pos, x + up.X * .20f, y + up.Y * .20f, z + up.Z * .20f,
                    x + up.X * .27f, y + up.Y * .27f, z + up.Z * .27f, unchecked((int)0xffe6a6ef));
            }
            if (host?.Connected(port) == true && host.PortRole(port) is "input" or "output")
                Arrow(pos, normal, host.PortRole(port) == "input");
        }
    }
    private void Frame(BlockPos pos)
    {
        for (int axis = 0; axis < 3; axis++)
        for (int a = 0; a < 2; a++)
        for (int b = 0; b < 2; b++)
        {
            var from = new float[3]; var to = new float[3];
            from[axis] = .02f; to[axis] = .98f;
            from[(axis + 1) % 3] = to[(axis + 1) % 3] = .02f + .96f * a;
            from[(axis + 2) % 3] = to[(axis + 2) % 3] = .02f + .96f * b;
            api!.Render.RenderLine(pos, from[0], from[1], from[2], to[0], to[1], to[2], unchecked((int)0xffe6a6ef));
        }
    }
    private void Arrow(BlockPos pos, Vec3i n, bool entering, int? color = null)
    {
        float start = entering ? .75f : .10f, end = entering ? .10f : .75f;
        var side = n.Y == 0 ? new Vec3i(0, 1, 0) : new Vec3i(1, 0, 0);
        void Line(float a, float b, float wing) => api!.Render.RenderLine(pos,
            .5f + n.X * a, .5f + n.Y * a, .5f + n.Z * a,
            .5f + n.X * b + side.X * wing, .5f + n.Y * b + side.Y * wing, .5f + n.Z * b + side.Z * wing,
            color ?? (entering ? unchecked((int)0xffedcc75) : unchecked((int)0xff79e2d0)));
        Line(start, end, 0);
        float back = end + (entering ? .15f : -.15f);
        Line(end, back, .11f); Line(end, back, -.11f);
    }
    public override void Dispose() { api?.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition); api = null; }
}
