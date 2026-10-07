using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Gearwright.Audio;

namespace Gearwright.Pneumatics;

public sealed partial class BlockEntityPneumaticTransport : BlockEntity
{
    internal PneumaticState State = new();
    internal PneumaticRouterState Router = new();
    internal PneumaticStockkeeperState Stockkeeper = new();
    internal bool IsSmartReceiver => Block.Code.Path == "pneumatic-smart-receiver";
    internal string Status = "idle";
    internal double Air;
    internal long NextAdvance;
    internal bool PreferOutlet;
    private PneumaticRenderer? renderer;
    private LocalMachineLoop? airflowSound;
    private PneumaticMachineSoundController? workSound;
    private long soundTick;
    internal PneumaticLineKind Kind => Block.Code.Path == "pneumatic-router" ? PneumaticLineKind.Router :
        Block.Code.Path == "pneumatic-sender" ? PneumaticLineKind.Sender :
        Block.Code.Path is "pneumatic-receiver" or "pneumatic-smart-receiver" ? PneumaticLineKind.InlineReceiver : PneumaticLineKind.Tube;
    internal PneumaticPosition Position => PneumaticNetworkSystem.Position(Pos);
    internal bool CanWrite => State.Writable && (Kind != PneumaticLineKind.Router || Router.Writable) && (!IsSmartReceiver || Stockkeeper.Writable);
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
            airflowSound = new(client, Pos, "airflow");
            workSound = new(this, client);
            soundTick = RegisterGameTickListener(seconds =>
            {
                airflowSound?.Update(seconds, CanWrite ? Math.Sqrt(Math.Clamp(Air / PneumaticAir.OutletUnitsPerSecond, 0, 1)) : 0);
                workSound?.Update(seconds);
            }, 100);
        }
        else api.ModLoader.GetModSystem<PneumaticNetworkSystem>().Register(this);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree, world);
        State = PneumaticState.Read(tree[PneumaticState.Key], world);
        if (Kind == PneumaticLineKind.Router) Router = PneumaticRouterState.Read(tree[PneumaticRouterState.Key]);
        if (IsSmartReceiver) Stockkeeper = PneumaticStockkeeperState.Read(tree[PneumaticStockkeeperState.Key], world);
        Status = tree.GetString("gearwrightPneumaticStatus", "idle");
        Air = tree.GetDouble("gearwrightPneumaticAir");
        renderer?.OnStateUpdated();
        stockkeeperDialog?.Refresh();
        if (!CanWrite) world.Logger.Error("[Gearwright] Pneumatic state at {0} is unreadable; transfers paused and original data preserved.", Pos);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree[PneumaticState.Key] = State.Write();
        if (Kind == PneumaticLineKind.Router) tree[PneumaticRouterState.Key] = Router.Write();
        if (IsSmartReceiver) tree[PneumaticStockkeeperState.Key] = Stockkeeper.Write();
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
        if (soundTick != 0) { UnregisterGameTickListener(soundTick); soundTick = 0; }
        airflowSound?.Dispose(); airflowSound = null;
        workSound?.Dispose(); workSound = null;
        routerDialog?.TryClose(); routerDialog?.Dispose(); routerDialog = null;
        stockkeeperDialog?.TryClose(); stockkeeperDialog?.Dispose(); stockkeeperDialog = null;
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
