using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;

namespace Gearwright.Pneumatics;

public sealed partial class BlockEntityPneumaticTransport : BlockEntity
{
    internal PneumaticState State = new();
    internal PneumaticRouterState Router = new();
    internal string Status = "idle";
    internal double Air;
    internal long NextAdvance;
    internal bool PreferOutlet;
    private PneumaticRenderer? renderer;
    internal PneumaticLineKind Kind => Block.Code.Path == "pneumatic-router" ? PneumaticLineKind.Router :
        Block.Code.Path == "pneumatic-sender" ? PneumaticLineKind.Sender :
        Block.Code.Path == "pneumatic-receiver" ? PneumaticLineKind.InlineReceiver : PneumaticLineKind.Tube;
    internal PneumaticPosition Position => PneumaticNetworkSystem.Position(Pos);
    internal bool CanWrite => State.Writable && (Kind != PneumaticLineKind.Router || Router.Writable);
    internal PneumaticLineNode Node => new(Position, Kind, State.Input, State.Output, true,
        CanWrite && (Kind is PneumaticLineKind.Tube or PneumaticLineKind.Router || State.Output == State.Input.Opposite && State.InventoryFace.Axis != State.Output.Axis),
        Kind == PneumaticLineKind.Router ? RouterPorts("input") : null, Kind == PneumaticLineKind.Router ? RouterPorts("output") : null);
    internal PneumaticInventory? Chest => PneumaticInventory.At(Api.World, Pos.AddCopy(State.InventoryFace));
    internal PneumaticInventory? OutletChest => Kind == PneumaticLineKind.Router ? null : PneumaticInventory.At(Api.World, Pos.AddCopy(State.Output));
    internal bool IsReceiving => Kind == PneumaticLineKind.InlineReceiver && !State.DeliveryOutlet &&
        (State.Destination == State.Instance || State.Destination == "" ||
            State.Route.Length > 0 && State.RouteIndex == State.Route.Length - 1);

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api is ICoreClientAPI client)
        {
            renderer = new PneumaticRenderer(this, client);
            client.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-pneumatic");
            client.Event.RegisterRenderer(renderer, EnumRenderStage.OIT, "gearwright-pneumatic-glass");
        }
        else api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Register(this);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        State = PneumaticState.Read(tree[PneumaticState.Key], world);
        if (Kind == PneumaticLineKind.Router) Router = PneumaticRouterState.Read(tree[PneumaticRouterState.Key]);
        Status = tree.GetString("gearwrightPneumaticStatus", "idle");
        Air = tree.GetDouble("gearwrightPneumaticAir");
        renderer?.OnStateUpdated();
        if (!CanWrite) world.Logger.Error("[Gearwright] Pneumatic state at {0} is unreadable; transfers paused and original data preserved.", Pos);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree[PneumaticState.Key] = State.Write();
        if (Kind == PneumaticLineKind.Router) tree[PneumaticRouterState.Key] = Router.Write();
        tree.SetString("gearwrightPneumaticStatus", Status);
        tree.SetDouble("gearwrightPneumaticAir", Air);
    }

    public override bool OnTesselation(ITerrainMeshPool pool, ITesselatorAPI tesselator) => true;
    public override void OnBlockBroken(IPlayer? byPlayer = null)
    {
        if (Api.Side == EnumAppSide.Server) Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Drop(this);
        base.OnBlockBroken(byPlayer);
    }
    public override void OnBlockRemoved() { Unregister(); base.OnBlockRemoved(); }
    public override void OnBlockUnloaded() { Unregister(); base.OnBlockUnloaded(); }
    private void Unregister()
    {
        routerDialog?.TryClose(); routerDialog?.Dispose(); routerDialog = null;
        if (Api is ICoreClientAPI client && renderer != null)
        {
            client.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            client.Event.UnregisterRenderer(renderer, EnumRenderStage.OIT);
            renderer.Dispose(); renderer = null;
        }
        else if (Api != null) Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Unregister(this);
    }
    public override void GetBlockInfo(IPlayer player, StringBuilder text)
    {
        base.GetBlockInfo(player, text);
        text.AppendLine(Lang.Get("gearwright:pneumatic-status-" + (CanWrite ? Status : "protected-state")));
        if (Kind == PneumaticLineKind.Router)
        { text.AppendLine(Lang.Get("gearwright:router-port-info", Router.SupplyPort, RouterOutputs.Length)); return; }
        text.AppendLine(Lang.Get("gearwright:pneumatic-direction", State.Input.Code, State.Output.Code));
        if (Kind != PneumaticLineKind.Tube) text.AppendLine(Lang.Get("gearwright:pneumatic-inventory-direction", State.InventoryFace.Code));
        if (OutletChest != null) text.AppendLine(Lang.Get("gearwright:pneumatic-outlet-receiving", State.Output.Code));
        if (State.Cargo != null) text.AppendLine(State.Cargo.StackSize + " × " + State.Cargo.GetName());
    }
}
