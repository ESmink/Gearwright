using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Pneumatics;

public sealed partial class BlockEntityPneumaticTransport
{
    private GuiDialogPneumaticRouter? routerDialog;
    internal int SelectedRouterPort;
    internal int RouterSearchCursor;
    internal int RouterSupplierCursor;
    internal void OpenRouterDialog(int port)
    {
        if (Api is not ICoreClientAPI client || routerDialog?.IsOpened() == true) return;
        routerDialog?.Dispose();
        routerDialog = new(this, client, Math.Clamp(port, 1, 4)); routerDialog.TryOpen();
    }
    internal void SendRouterConfiguration(PneumaticRouterConfiguration configuration)
    {
        if (Api is ICoreClientAPI client) client.Network.SendBlockEntityPacket(Pos, 2102,
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(configuration)));
    }
    public override void OnReceivedClientPacket(IPlayer fromPlayer, int packetid, byte[] data)
    {
        base.OnReceivedClientPacket(fromPlayer, packetid, data);
        if (packetid == 2103) { ReceiveStockkeeperConfiguration(fromPlayer, data); return; }
        if (Api.Side != EnumAppSide.Server || packetid != 2102 || Kind != PneumaticLineKind.Router || !CanWrite || data.Length > 16384 ||
            !PneumaticPlacement.InReach(fromPlayer, Pos) || !Api.World.Claims.TryAccess(fromPlayer, Pos, EnumBlockAccessFlags.Use) ||
            Router.NextConfiguration > Api.World.ElapsedMilliseconds) return;
        Router.NextConfiguration = Api.World.ElapsedMilliseconds + 300;
        PneumaticRouterConfiguration? configuration;
        try { configuration = PneumaticRouterConfiguration.Parse(Encoding.UTF8.GetString(data)); }
        catch { return; }
        if (configuration?.Valid != true) return;
        Api.ModLoader.GetModSystem<PneumaticNetworkSystem>().ConfigureRouter(this, configuration);
    }
    internal BlockEntity? PortNeighbour(int port)
    {
        var position = Pos.AddCopy(Router.Face(port));
        return Api.World.BlockAccessor.GetChunkAtBlockPos(position) == null ? null : Api.World.BlockAccessor.GetBlockEntity(position);
    }
    internal bool Connected(int port)
    {
        var face = Router.Face(port).Opposite;
        return PortNeighbour(port) switch
        {
            BlockEntityPneumaticAirIntake intake => intake.Outlet == face,
            BlockEntityPneumaticTransport h when h.Kind == PneumaticLineKind.Router => h.Router.Port(face) != 0,
            BlockEntityPneumaticTransport h => h.State.Input == face || h.State.Output == face,
            _ => false
        };
    }
    internal int ConnectionMask => Enumerable.Range(1, 4).Aggregate(0, (mask, n) => mask | (Connected(n) ? 1 << (n - 1) : 0));
    internal string PortRole(int port)
    {
        var face = Router.Face(port).Opposite;
        return PortNeighbour(port) switch
        {
            BlockEntityPneumaticAirIntake intake when intake.Outlet == face => "input",
            BlockEntityPneumaticTransport h when h.Kind != PneumaticLineKind.Router =>
                h.State.Output == face ? "input" : h.State.Input == face ? "output" : "closed",
            BlockEntityPneumaticTransport h when h.Router.Port(face) is int n && n > 0 =>
                PneumaticRouterPortRoles.Between(this, h),
            _ => "closed"
        };
    }
    internal BlockFacing[] RouterPorts(string role) => Enumerable.Range(1, 4)
        .Where(n => Connected(n) && PortRole(n) == role).Select(Router.Face).ToArray();
    internal int[] RouterOutputs => RouterPorts("output").Select(Router.Port).ToArray();
}
