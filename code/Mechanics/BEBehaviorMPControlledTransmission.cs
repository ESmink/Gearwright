using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>
/// Keeps a transmission's two Vanilla networks separate and exchanges speed only while its clutch is engaged.
/// </summary>
public sealed class BEBehaviorMPControlledTransmission : BEBehaviorMPTransmission
{
    private const int SolverIntervalMilliseconds = 50;
    private const long BroadcastIntervalMilliseconds = 100;
    private readonly ClutchTerminal firstTerminal;
    private readonly ClutchTerminal secondTerminal;
    private long solverListenerId;
    private long lastBroadcastMilliseconds;
    private bool pendingBroadcast;
    private float firstLocalFactor = 1;
    private float secondLocalFactor = 1;
    private float lastTransfer;

    public BEBehaviorMPControlledTransmission(BlockEntity blockentity) : base(blockentity)
    {
        firstTerminal = new ClutchTerminal(blockentity);
        secondTerminal = new ClutchTerminal(blockentity);
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        // Vanilla's Initialize merges the two sides when this field is true. Hide the
        // saved state for that call, then restore it for rendering and our solver.
        bool savedEngaged = engaged;
        engaged = false;
        base.Initialize(api, properties);
        engaged = savedEngaged;

        // A pre-Gearwright save may still carry the old through-network id. The
        // controlled transmission itself must never remain a member of that network.
        if (Network != null) LeaveNetwork();
        NetworkId = 0;

        if (api.Side == EnumAppSide.Server)
        {
            // Neighbour block entities in the same loading chunk may not have
            // initialized their Vanilla mechanical-power manager yet. Defer all
            // topology discovery to the first scheduled server tick.
            solverListenerId = Blockentity.RegisterGameTickListener(
                OnServerTick, SolverIntervalMilliseconds);
        }
    }

    public override MechPowerPath[] GetMechPowerExits(MechPowerPath path) =>
        Array.Empty<MechPowerPath>();

    public override float AngleRad
    {
        get
        {
            foreach (BlockFacing face in AxisFaces())
            {
                BEBehaviorMPBase? neighbour = Api?.World.BlockAccessor
                    .GetBlockEntity(Position.AddCopy(face))?
                    .GetBehavior<BEBehaviorMPBase>();
                if (neighbour?.Network?.Valid == true &&
                    neighbour is not BEBehaviorMPControlledTransmission)
                {
                    return neighbour.AngleRad;
                }
            }
            return base.AngleRad;
        }
    }

    public void SetControlledEngaged(bool value)
    {
        if (Api?.Side != EnumAppSide.Server || engaged == value) return;
        engaged = value;
        pendingBroadcast = true;
        Blockentity.MarkDirty(true);
        BroadcastIfDue(force: true);
    }

    public void RefreshNow()
    {
        if (Api?.Side != EnumAppSide.Server) return;
        RefreshEngagedFromClutch();
        RefreshPorts();
    }

    public override void OnBlockRemoved()
    {
        Shutdown();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        Shutdown();
        base.OnBlockUnloaded();
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);
        string state = engaged
            ? Lang.Get("gearwright:controlled-clutch-engaged")
            : Lang.Get("gearwright:controlled-clutch-disengaged");
        sb.AppendLine(state);
        AppendTerminalStatus(sb);
    }

    private void OnServerTick(float elapsedSeconds)
    {
        RefreshEngagedFromClutch();
        RefreshPorts();

        MechanicalNetwork? firstNetwork = firstTerminal.Network;
        MechanicalNetwork? secondNetwork = secondTerminal.Network;
        if (firstNetwork?.Valid == true && secondNetwork?.Valid == true &&
            !ReferenceEquals(firstNetwork, secondNetwork))
        {
            float firstSpeed = firstNetwork.Speed * firstLocalFactor;
            float secondSpeed = secondNetwork.Speed * secondLocalFactor;
            if (engaged)
            {
                ClutchCouplingStep step = ClutchCouplingMath.Solve(
                    firstSpeed, secondSpeed, elapsedSeconds);
                if (step.Changed)
                {
                    firstNetwork.Speed = step.FirstSpeed / firstLocalFactor;
                    secondNetwork.Speed = step.SecondSpeed / secondLocalFactor;
                    lastTransfer = step.Transfer;
                    pendingBroadcast = true;
                }
            }
            else
            {
                lastTransfer = 0;
            }
        }
        else
        {
            lastTransfer = 0;
        }

        BroadcastIfDue(force: false);
    }

    private void RefreshPorts()
    {
        BlockFacing[] faces = AxisFaces();
        PortCandidate first = ResolvePort(faces[0]);
        PortCandidate second = ResolvePort(faces[1]);

        if (first.Network != null && ReferenceEquals(first.Network, second.Network))
        {
            // An external shaft path already joins the sides. A MechanicalNetwork is
            // keyed by position, so only one terminal may occupy our position there.
            Attach(firstTerminal, first.Network);
            secondTerminal.LeaveNetwork();
            firstLocalFactor = first.LocalFactor;
            secondLocalFactor = second.LocalFactor;
            return;
        }

        AttachOrLeave(firstTerminal, first.Network);
        AttachOrLeave(secondTerminal, second.Network);
        firstLocalFactor = first.LocalFactor;
        secondLocalFactor = second.LocalFactor;
    }

    private PortCandidate ResolvePort(BlockFacing face)
    {
        BlockPos neighbourPosition = Position.AddCopy(face);
        Block block = Api.World.BlockAccessor.GetBlock(neighbourPosition);
        if (block is not BlockMPBase mechanicalBlock ||
            !mechanicalBlock.HasMechPowerConnectorAt(
                Api.World,
                neighbourPosition,
                face.Opposite,
                (BlockMPBase)Blockentity.Block))
        {
            return default;
        }

        BEBehaviorMPBase? neighbour = Api.World.BlockAccessor
            .GetBlockEntity(neighbourPosition)?
            .GetBehavior<BEBehaviorMPBase>();
        MechanicalNetwork? network = neighbour?.Network;
        if (neighbour != null && network?.Valid != true)
        {
            // A lone output axle normally receives its first network when it
            // connects through a Vanilla transmission. This boundary is hidden
            // from Vanilla discovery, so bootstrap that shaft's own network
            // from the valid neighbour instead. The controlled transmission's
            // false connector and empty exits still stop propagation here.
            network = neighbour.CreateJoinAndDiscoverNetwork(face.Opposite);
        }
        if (network?.Valid != true) return default;

        float factor = neighbour!.GearedRatio * (neighbour.IsRotationReversed() ? -1 : 1);
        if (!float.IsFinite(factor) || Math.Abs(factor) < ClutchCouplingMath.SpeedEpsilon)
        {
            factor = 1;
        }
        return new PortCandidate(network, factor);
    }

    private void RefreshEngagedFromClutch()
    {
        BlockFacing[] axis = AxisFaces();
        bool foundEngaged = false;
        foreach (BlockFacing face in BlockFacing.HORIZONTALS)
        {
            if (face == axis[0] || face == axis[1]) continue;
            if (Api.World.BlockAccessor.GetBlockEntity(Position.AddCopy(face)) is not BEControlledClutch clutch ||
                clutch.Facing != face.Opposite)
            {
                continue;
            }
            foundEngaged = clutch.Engaged;
            break;
        }

        if (engaged == foundEngaged) return;
        engaged = foundEngaged;
        pendingBroadcast = true;
        Blockentity.MarkDirty(true);
    }

    private BlockFacing[] AxisFaces()
    {
        return Blockentity.Block.Variant["orientation"] == "we"
            ? new[] { BlockFacing.WEST, BlockFacing.EAST }
            : new[] { BlockFacing.NORTH, BlockFacing.SOUTH };
    }

    private void AppendTerminalStatus(StringBuilder sb)
    {
        if (firstTerminal.Network == null || secondTerminal.Network == null)
        {
            sb.AppendLine(Lang.Get("gearwright:controlled-clutch-waiting"));
        }
        else if (ReferenceEquals(firstTerminal.Network, secondTerminal.Network))
        {
            sb.AppendLine(Lang.Get("gearwright:controlled-clutch-bypassed"));
        }
        else if (engaged)
        {
            sb.AppendLine(Lang.Get(
                "gearwright:controlled-clutch-transfer",
                Math.Abs(lastTransfer)));
        }
    }


    private void AttachOrLeave(ClutchTerminal terminal, MechanicalNetwork? network)
    {
        if (network == null)
        {
            terminal.LeaveNetwork();
            return;
        }
        Attach(terminal, network);
    }

    private void Attach(ClutchTerminal terminal, MechanicalNetwork network)
    {
        if (network.nodes.TryGetValue(Position, out IMechanicalPowerNode? occupant) &&
            ReferenceEquals(occupant, this))
        {
            LeaveNetwork();
        }
        terminal.Attach(network);
    }

    private void BroadcastIfDue(bool force)
    {
        if (!pendingBroadcast) return;
        long now = Api.World.ElapsedMilliseconds;
        if (!force && now - lastBroadcastMilliseconds < BroadcastIntervalMilliseconds) return;

        MechanicalNetwork? first = firstTerminal.Network;
        MechanicalNetwork? second = secondTerminal.Network;
        first?.broadcastData();
        if (second != null && !ReferenceEquals(first, second)) second.broadcastData();
        lastBroadcastMilliseconds = now;
        pendingBroadcast = false;
    }

    private void Shutdown()
    {
        if (solverListenerId != 0)
        {
            Blockentity.UnregisterGameTickListener(solverListenerId);
            solverListenerId = 0;
        }
        firstTerminal.LeaveNetwork();
        secondTerminal.LeaveNetwork();
    }

    private readonly record struct PortCandidate(
        MechanicalNetwork? Network,
        float LocalFactor);
}
