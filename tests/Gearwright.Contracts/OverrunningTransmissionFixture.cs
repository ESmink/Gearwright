using System;
using System.Collections.Generic;
using System.Text;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent.Mechanics;
using static Gearwright.Contracts.PumpRuntimeFixture;

namespace Gearwright.Contracts;

internal static class OverrunningTransmissionFixture
{
    internal static void Run(Action<bool, string> check)
    {
        const string locale = "gearwright-transmission-contract";
        string previousLocale = Lang.CurrentLocale;
        Lang.AvailableLanguages[locale] = Stub.Create<ITranslationService>((method, args) => method.Name switch
        { "HasTranslation" => true, "Get" => args![0], _ => null });
        var localeProperty = typeof(Lang).GetProperty(nameof(Lang.CurrentLocale))!;
        localeProperty.SetValue(null, locale);
        try { RunScenarios(check); }
        finally { localeProperty.SetValue(null, previousLocale); Lang.AvailableLanguages.Remove(locale); }
    }

    private static void RunScenarios(Action<bool, string> check)
    {
        foreach (int hand in new[] { 1, -1 })
        {
            var rig = new Rig();
            check(!rig.Client.TryGetDriveState(out _), "An unsynchronized client does not guess the ratchet state");
            rig.Input.Speed = hand * .3f; rig.Output.Speed = hand * .1f;
            rig.Tick(); rig.Deliver();
            check(rig.Client.TryGetDriveState(out var drive) && drive.Engaged && drive.Handedness == hand,
                "The actual server coupling publishes engagement and handedness: " + hand);

            // Once coupled, equal speeds no longer contain evidence of the lock.
            rig.Input.Speed = rig.Output.Speed = hand * .2f;
            for (int i = 0; i < 24; i++) rig.Tick();
            rig.Deliver();
            var info = new StringBuilder(); rig.Client.GetBlockInfo(null!, info);
            check(rig.Client.TryGetDriveState(out drive) && drive.Engaged &&
                info.ToString().Contains(Lang.Get("gearwright:overrunning-engaged")),
                "A synchronized loaded transmission still reports driving output: " + hand);
            check(rig.Packets.Count == 2, "Unchanged engagement uses one heartbeat per second, not one packet per tick");
            var lateClient = rig.NewClient();
            lateClient.OnReceivedServerPacket(BEBehaviorMPOverrunningTransmission.DriveStatePacketId, rig.Packets[^1]);
            check(lateClient.TryGetDriveState(out drive) && drive.Engaged,
                "A late observer learns the lock even when the last solver step exchanged no speed");

            rig.Output.AngleRad = -hand * OverrunningPawlMath.FullContactThreatPhaseLag;
            rig.Input.Speed = hand * .2f; rig.Output.Speed = hand * .25f;
            rig.Tick(); rig.Deliver();
            check(rig.Server.TryGetDriveState(out drive) && drive.Engaged &&
                rig.Client.TryGetDriveState(out drive) && drive.Engaged,
                "Loaded tooth-phase recovery stays engaged despite an output speed lead above the ordinary release threshold");

            rig.Input.Speed = hand * .2f; rig.Output.Speed = hand * .4f;
            rig.Tick(); rig.Tick(); rig.Deliver();
            info.Clear(); rig.Client.GetBlockInfo(null!, info);
            check(rig.Client.TryGetDriveState(out drive) && !drive.Engaged &&
                info.ToString().Contains(Lang.Get("gearwright:overrunning-freewheeling")),
                "Genuine output overrun publishes release and updates the tooltip: " + hand);

            rig.Now += 3001;
            check(!rig.Client.TryGetDriveState(out _), "Expired server state cannot drive stale ratchet sounds");
            rig.Tick(); rig.Deliver();
            check(rig.Client.TryGetDriveState(out _), "The next heartbeat restores presentation after a pause");
            rig.Client.OnReceivedServerPacket(BEBehaviorMPOverrunningTransmission.DriveStatePacketId, new byte[] { 2, 0 });
            check(!rig.Client.TryGetDriveState(out _), "Unknown or truncated drive packets disable presentation safely");
            rig.Deliver();

            rig.Output.networkId = 3;
            check(!rig.Client.TryGetDriveState(out _), "A snapshot for the old output network cannot describe a rebuilt connection");
            Set(rig.OutputPort, "network", rig.Input);
            rig.Tick(); rig.Tick(); rig.Deliver();
            check(!rig.Server.TryGetDriveState(out _) && !rig.Client.TryGetDriveState(out _),
                "A bypassed transmission clears the authoritative lock and its client presentation");
            var tree = new TreeAttribute(); rig.Server.ToTreeAttributes(tree);
            check(tree.Count == 0, "Ratchet presentation introduces no saved attributes or schema");
            rig.Server.OnBlockUnloaded(); rig.Client.OnBlockUnloaded();
            check(!rig.Client.TryGetDriveState(out _), "Unloading releases transient ratchet presentation");
        }
        foreach (var state in new[] { OverrunningDriveState.Unavailable,
            new OverrunningDriveState(true, false, -1, 11, 12), new(true, true, 1, 11, 12) })
            check(OverrunningDriveState.TryDecode(state.Encode(), out var decoded) && decoded == state,
                "Version-one drive packets round-trip their state and network identities");
        byte[] invalid = new OverrunningDriveState(true, true, 1, 11, 12).Encode();
        invalid[1] = 2;
        check(!OverrunningDriveState.TryDecode(invalid, out _), "An unavailable drive packet cannot claim engagement");
    }

    private sealed class Rig
    {
        internal readonly MechanicalNetwork Input = new() { networkId = 1, Valid = true };
        internal readonly MechanicalNetwork Output = new() { networkId = 2, Valid = true };
        internal readonly BEBehaviorMPLateralCrank OutputPort = null!;
        internal readonly BEBehaviorMPOverrunningTransmission Server, Client;
        internal readonly List<byte[]> Packets = new();
        internal long Now;
        private Action<float> tick = null!;
        private readonly BlockOverrunningTransmission block;
        private readonly ICoreClientAPI clientApi;

        internal Rig()
        {
            var entities = new Dictionary<BlockPos, BlockEntity>();
            var accessor = Stub.Create<IBlockAccessor>((method, args) => method.Name switch
            {
                "GetBlockEntity" => entities.GetValueOrDefault((BlockPos)args![0]!),
                "GetBlock" => entities.GetValueOrDefault((BlockPos)args![0]!)?.Block ?? new Block(),
                _ => null
            });
            var serverWorld = Stub.Create<IServerWorldAccessor>((method, _) => method.Name switch
            { "get_BlockAccessor" => accessor, "get_ElapsedMilliseconds" => Now, _ => null });
            var events = Stub.Create<IServerEventAPI>((method, args) =>
            {
                if (method.Name == "RegisterGameTickListener") { tick = (Action<float>)args![0]!; return 1L; }
                return null;
            });
            var network = Stub.Create<IServerNetworkAPI>((method, args) =>
            {
                if (method.Name == "BroadcastBlockEntityPacket") Packets.Add((byte[])args![2]!);
                return null;
            });
            var api = Stub.Create<ICoreServerAPI>((method, _) => method.Name switch
            { "get_World" => serverWorld, "get_Event" => events, "get_Network" => network,
              "get_Side" => EnumAppSide.Server, _ => null });
            var clientWorld = Stub.Create<IClientWorldAccessor>((method, _) => method.Name switch
            { "get_BlockAccessor" => accessor, "get_ElapsedMilliseconds" => Now, _ => null });
            clientApi = Stub.Create<ICoreClientAPI>((method, _) => method.Name switch
            { "get_World" => clientWorld, "get_Side" => EnumAppSide.Client, _ => null });
            block = new BlockOverrunningTransmission { Variant = new RelaxedReadOnlyDictionary<string, string>(
                new Dictionary<string, string> { ["orientation"] = "we", ["input"] = "negative" }) };
            var owner = new FixtureEntity { Api = api, Pos = new BlockPos(0, 0, 0) };
            Set(owner, "Block", block);
            Server = new(owner); Server.Initialize(api, JsonObject.FromJson("{}"));
            Client = NewClient();
            foreach (var pair in new[] { (Face: BlockFacing.WEST, Network: Input), (Face: BlockFacing.EAST, Network: Output) })
            {
                var entity = new FixtureEntity { Api = api, Pos = owner.Pos.AddCopy(pair.Face) };
                Set(entity, "Block", new FixtureMechanicalBlock());
                var port = new BEBehaviorMPLateralCrank(entity);
                Set(port, "Api", api); Set(port, "gearedRatio", 1f); Set(port, "network", pair.Network);
                entity.Behaviors.Add(port); entities.Add(entity.Pos, entity);
                if (pair.Face == BlockFacing.EAST) OutputPort = port;
                var manager = new MechanicalPowerMod();
                Set(manager, "serverNwChannel", Stub.Create<IServerNetworkChannel>((_, _) => null));
                Set(pair.Network, "mechanicalPowerMod", manager);
            }
        }

        internal BEBehaviorMPOverrunningTransmission NewClient()
        {
            var owner = new FixtureEntity { Api = clientApi, Pos = new BlockPos(0, 0, 0) };
            Set(owner, "Block", block);
            var behavior = new BEBehaviorMPOverrunningTransmission(owner);
            Set(behavior, "Api", clientApi); // No graphics context is needed to receive runtime packets.
            return behavior;
        }
        internal void Tick() { Now += 50; tick(.05f); }
        internal void Deliver() => Client.OnReceivedServerPacket(BEBehaviorMPOverrunningTransmission.DriveStatePacketId, Packets[^1]);
    }

    private sealed class FixtureEntity : BlockEntity { }
    private sealed class FixtureMechanicalBlock : BlockMPBase
    {
        public override bool HasMechPowerConnectorAt(IWorldAccessor world, BlockPos pos, BlockFacing face,
            BlockMPBase? fromBlock) => true;
        public override void DidConnectAt(IWorldAccessor world, BlockPos pos, BlockFacing face) { }
    }
}
