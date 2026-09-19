using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gearwright.Hydraulics;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>Coordinates only loaded networks containing Gearwright piston pumps.</summary>
public sealed class PumpTimingSystem : ModSystem
{
    private const string PatchId = "gearwright.pump-timing";
    private const string Channel = "gearwright-pump-motion";
    private static readonly object Gate = new();
    private static Harmony? harmony;
    private static int users;
    private static PumpTimingSystem? server;
    private static PumpTimingSystem? client;
    private static Action<MechanicalNetwork, long> updateNetwork = null!;
    private static Action<MechanicalNetwork, float> updateAngle = null!;
    private static Action<MechanicalNetwork> broadcastData = null!;
    private bool registered;
    private ICoreServerAPI? sapi;
    private ICoreClientAPI? capi;
    private IServerNetworkChannel? serverChannel;
    private readonly Dictionary<long, PumpPresentationTimeline> timelines = new();
    private readonly ConditionalWeakTable<MechanicalNetwork, PumpDriveLoad> driveLoads = new();
    private long sequence;

    private sealed class PumpDriveLoad
    {
        public double NonFluidResistance;
    }

    public override void Start(ICoreAPI api)
    {
        lock (Gate)
        {
            if (users == 0)
            {
                static MethodInfo Required(string name) => AccessTools.Method(typeof(MechanicalNetwork), name)
                    ?? throw new MissingMethodException("Pump timing requires MechanicalNetwork." + name);
                updateNetwork = Required("updateNetwork").CreateDelegate<Action<MechanicalNetwork, long>>();
                updateAngle = Required("UpdateAngle").CreateDelegate<Action<MechanicalNetwork, float>>();
                broadcastData = Required("broadcastData").CreateDelegate<Action<MechanicalNetwork>>();
                Harmony patcher = new(PatchId);
                try
                {
                    patcher.Patch(Required(nameof(MechanicalNetwork.ServerTick)),
                        prefix: new HarmonyMethod(typeof(PumpTimingSystem), nameof(BeforeServerTick)));
                    patcher.Patch(Required(nameof(MechanicalNetwork.ClientTick)),
                        prefix: new HarmonyMethod(typeof(PumpTimingSystem), nameof(BeforeClientTick)));
                    patcher.Patch(Required(nameof(MechanicalNetwork.UpdateFromPacket)),
                        postfix: new HarmonyMethod(typeof(PumpTimingSystem), nameof(AfterNetworkPacket)));
                    harmony = patcher;
                }
                catch { patcher.UnpatchAll(PatchId); throw; }
            }
            users++;
            registered = true;
            if (api.Side == EnumAppSide.Server) server = this;
            else client = this;
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        serverChannel = api.Network.RegisterChannel(Channel).RegisterMessageType<PumpNetworkFrame>();
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        api.Network.RegisterChannel(Channel).RegisterMessageType<PumpNetworkFrame>()
            .SetMessageHandler<PumpNetworkFrame>(ReceiveFrame);
    }

    private void ReceiveFrame(PumpNetworkFrame frame)
    {
        if (!frame.IsValid || capi == null) return;
        long now = capi.World.ElapsedMilliseconds;
        // Bound stale caches after unload/topology changes without retaining a world.
        foreach (long id in timelines.Where(p => now - p.Value.LastReceivedAt > 10000).Select(p => p.Key).ToArray())
            timelines.Remove(id);
        if (!timelines.TryGetValue(frame.NetworkId, out PumpPresentationTimeline? timeline))
        {
            if (timelines.Count >= 256) return;
            timelines[frame.NetworkId] = timeline = new PumpPresentationTimeline();
        }
        timeline.Push(frame, now);
    }

    internal static bool TryPresentation(BlockEntityReciprocatingPump pump, out PumpPresentation presentation)
    {
        presentation = null!;
        MechanicalNetwork? network = pump.FindCrank()?.Network;
        return network != null && client != null && client.timelines.TryGetValue(network.networkId, out var timeline) &&
            timeline.TryPump(pump.Pos.X, pump.Pos.Y, pump.Pos.Z, pump.Pos.dimension, out presentation);
    }

    internal static bool HasPresentationNetwork(BlockEntityReciprocatingPump pump) =>
        pump.FindCrank()?.Network is MechanicalNetwork network &&
        client?.timelines.ContainsKey(network.networkId) == true;

    private static bool BeforeServerTick(MechanicalNetwork __instance, float __0, long __1)
    {
        var drives = __instance.nodes.Values.OfType<BEBehaviorMPLateralCrank>()
            .Select(d => (Drive: d, Devices: d.AttachedDevices)).Where(p => p.Devices.Length > 0).ToArray();
        if (drives.Length == 0 || server == null)
        {
            server?.driveLoads.Remove(__instance);
            return true;
        }
        if (!float.IsFinite(__0) || __0 <= 0) return false;
        PumpDriveLoad driveLoad = server.driveLoads.GetOrCreateValue(__instance);
        // Do not resample motors, clutches or stored flywheel momentum faster
        // than vanilla. Only fluid braking moves to the integration substeps.
        if (__1 % 5 == 0)
        {
            float incomingSpeed = __instance.DirectionHasReversed ? -__instance.Speed : __instance.Speed;
            updateNetwork(__instance, __1);
            driveLoad.NonFluidResistance = Math.Max(0, __instance.NetworkResistance -
                drives.Sum(p => Math.Abs(p.Drive.GearedRatio) * (double)p.Drive.SampledDeviceResistance));
            // Retain vanilla's torque sampling and bookkeeping, then integrate
            // its cached drive torque against live fluid load below. Applying
            // its capped speed impulse as well would count motor work twice.
            __instance.Speed = incomingSpeed;
        }
        double remaining = Math.Min(__0, .2);
        double inertia = Math.Max(1, Math.Pow(__instance.nodes.Count, .25));
        double maximumRatio = drives.Max(p => Math.Abs(p.Drive.GearedRatio));
        double lastFluidResistance = 0;
        // Bound both work per tick and angular travel per sample. A fast geared
        // journal must not jump across a narrow compression region near BDC.
        for (int i = 0; i < 256 && remaining > 1e-8; i++)
        {
            double speed = __instance.Speed;
            // At most .01 speed can be gained in a 20 ms substep. Include it
            // when bounding journal travel, including a geared start from rest.
            float seconds = (float)Math.Min(remaining, Math.Min(.02,
                .05 / Math.Max(1e-9, (Math.Abs(speed) + .01) * 5 * maximumRatio)));
            (double Torque, double Friction) DeviceForces(double trial)
            {
                double torque = 0, load = 0;
                foreach (var pair in drives)
                foreach (IReciprocatingDriveDevice device in pair.Devices)
                {
                    double ratio = pair.Drive.DeviceTravel(device, 1);
                    double travel = ratio * trial * seconds * 5;
                    torque += ratio * device.SampleReciprocatingTorque(pair.Drive.DeviceAngle(device), travel, seconds);
                    load += Math.Abs(pair.Drive.GearedRatio) * Math.Max(0,
                        device.SampleReciprocatingLoad(pair.Drive.DeviceAngle(device), travel, seconds));
                }
                return (torque, load);
            }
            // Backward Euler against the actual stored fluid at the proposed
            // end angle. Signed pressure can brake, hold or reverse the shaft.
            // Bound acceleration away from rest as vanilla does, but allow
            // pressure to consume all incoming momentum within this substep.
            double impulseScale = seconds / .1 / inertia;
            double accelerationCap = .05 * seconds / .1;
            double minimum = Math.Min(0, speed) - accelerationCap;
            double maximum = Math.Max(0, speed) + accelerationCap;
            double lower = minimum, upper = maximum;
            double Residual(double trial)
            {
                var force = DeviceForces(trial);
                double net = __instance.NetworkTorque + force.Torque -
                    Math.Sign(trial) * (driveLoad.NonFluidResistance + force.Friction);
                return trial - Math.Clamp(speed + net * impulseScale, minimum, maximum);
            }
            var rest = DeviceForces(0);
            double nextSpeed;
            if (Math.Abs(speed / impulseScale + __instance.NetworkTorque + rest.Torque) <=
                driveLoad.NonFluidResistance + rest.Friction) nextSpeed = 0;
            else
            {
                for (int iteration = 0; iteration < 32; iteration++)
                {
                    double trial = (lower + upper) * .5;
                    if (Residual(trial) > 0) upper = trial;
                    else lower = trial;
                }
                nextSpeed = (lower + upper) * .5;
            }
            lastFluidResistance = DeviceForces(nextSpeed).Friction;
            __instance.Speed = (float)nextSpeed;
            updateAngle(__instance, __instance.Speed * seconds * 50);
            foreach (var pair in drives)
                foreach (IReciprocatingDriveDevice device in pair.Devices) device.StepReciprocatingDrive(seconds);
            remaining -= seconds;
        }
        __instance.NetworkResistance = (float)(driveLoad.NonFluidResistance + lastFluidResistance);
        __instance.TurnDir = __instance.Speed < 0 ? EnumRotDirection.Counterclockwise : EnumRotDirection.Clockwise;
        var pumps = drives.SelectMany(pair => pair.Devices.OfType<BlockEntityReciprocatingPump>()
            .Select(pump => (pair.Drive, Pump: pump))).ToArray();
        if (__1 % 5 == 0 && pumps.Length > 0 && pumps.All(p => !p.Pump.UsesNetworkSolver))
            server.Publish(__instance, pumps.Select(p => p.Pump.CapturePresentation(p.Drive)).ToArray());
        // Standard packets still supply non-pump clients with torque and speed.
        if (__1 % 40 == 0) broadcastData(__instance);
        return false;
    }

    private void Publish(MechanicalNetwork network, PumpPresentation[] pumps)
    {
        if (sapi == null || serverChannel == null) return;
        IServerPlayer[] observers = sapi.World.AllOnlinePlayers.OfType<IServerPlayer>().Where(player => pumps.Any(p =>
            player.Entity.Pos.Dimension == p.Dimension &&
            player.Entity.Pos.SquareDistanceTo(p.X + .5, p.Y + .5, p.Z + .5) < 96 * 96)).ToArray();
        if (observers.Length == 0) return;
        serverChannel.SendPacket(new PumpNetworkFrame
        {
            NetworkId = network.networkId, Sequence = ++sequence, ServerMilliseconds = sapi.World.ElapsedMilliseconds,
            Angle = network.AngleRad, Speed = network.Speed, Pumps = pumps
        }, observers);
    }

    internal static void PublishHydraulicFrames(IEnumerable<BlockEntityReciprocatingPump> pumps)
    {
        if (server == null) return;
        foreach (var group in pumps.Select(p => (Pump: p, Drive: p.FindCrank()))
                     .Where(p => p.Drive?.Network != null).GroupBy(p => p.Drive!.Network!))
            server.Publish(group.Key, group.Select(p => p.Pump.CapturePresentation(p.Drive!)).ToArray());
    }

    private static bool BeforeClientTick(MechanicalNetwork __instance)
    {
        if (client?.capi == null || !client.timelines.TryGetValue(__instance.networkId, out var timeline)) return true;
        // Removing the last pump must immediately give its network back to the
        // vanilla clock instead of freezing it on the last pump snapshot.
        if (!client.HasLoadedPump(timeline, __instance))
        {
            client.timelines.Remove(__instance.networkId);
            return true;
        }
        timeline.Advance(client.capi.World.ElapsedMilliseconds);
        __instance.AngleRad = (float)timeline.Angle;
        __instance.Speed = Math.Abs(timeline.Speed);
        return false;
    }

    private static void AfterNetworkPacket(MechanicalNetwork __instance, MechNetworkPacket __0)
    {
        if (client?.capi != null && client.timelines.TryGetValue(__instance.networkId, out var timeline) &&
            client.HasLoadedPump(timeline, __instance))
        {
            // Vanilla packets have no sequence tied to the pump's contents.
            // They still supply torque/topology, but cannot move the axle off
            // the combined presentation clock, even between client ticks.
            timeline.Advance(client.capi.World.ElapsedMilliseconds);
            __instance.AngleRad = (float)timeline.Angle;
            __instance.Speed = Math.Abs(timeline.Speed);
            return;
        }
        if (Math.Abs(__0.speed) >= .001 || !__instance.nodes.Values.Any(n => n is BEBehaviorMPLateralCrank)) return;
        // Also fix stopped angles before the first combined snapshot arrives.
        __instance.AngleRad = __0.angle;
    }

    private bool HasLoadedPump(PumpPresentationTimeline timeline, MechanicalNetwork network) =>
        // Client devices can join via their saved network ID without entering
        // MechanicalNetwork.nodes, which is used for server simulation. Resolve
        // the bounded snapshot positions against loaded entities instead, and
        // verify their current network so removal/reconnection releases the clock.
        timeline.HasMatchingPump(p => capi!.World.BlockAccessor.GetBlockEntity(
            new BlockPos(p.X, p.Y, p.Z, p.Dimension)) is BlockEntityReciprocatingPump pump &&
            ReferenceEquals(pump.FindCrank()?.Network, network));

    public override void Dispose()
    {
        timelines.Clear();
        driveLoads.Clear();
        lock (Gate)
        {
            if (!registered) return;
            registered = false;
            if (ReferenceEquals(server, this)) server = null;
            if (ReferenceEquals(client, this)) client = null;
            if (--users == 0) { harmony?.UnpatchAll(PatchId); harmony = null; }
        }
    }
}
