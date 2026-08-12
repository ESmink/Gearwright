using System;
using System.Text;
using Vintagestory.API.Client;
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
    private OverrunningTransmissionRenderer? overrunningRenderer;

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
        else if (api is ICoreClientAPI capi && IsOverrunning)
        {
            overrunningRenderer = new OverrunningTransmissionRenderer(this, capi);
            capi.Event.RegisterRenderer(
                overrunningRenderer,
                EnumRenderStage.Opaque,
                "gearwright-overrunning-transmission");
        }
    }

    public override void SetOrientations()
    {
        if (Blockentity.Block is BlockOverrunningTransmission transmission)
        {
            BlockFacing input = transmission.InputFace;
            AxisSign = new[] { input.Normali.X, input.Normali.Y, input.Normali.Z };
            return;
        }
        base.SetOrientations();
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
        if (IsOverrunning || Api?.Side != EnumAppSide.Server || engaged == value) return;
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
        if (IsOverrunning)
        {
            sb.AppendLine(Lang.Get(
                "gearwright:overrunning-input-face",
                Lang.Get("direction-" + AxisFaces()[0].Code)));
            sb.AppendLine(engaged
                ? Lang.Get("gearwright:overrunning-engaged")
                : Lang.Get("gearwright:overrunning-freewheeling"));
            AppendTerminalStatus(sb);
            return;
        }
        string state = engaged
            ? Lang.Get("gearwright:controlled-clutch-engaged")
            : Lang.Get("gearwright:controlled-clutch-disengaged");
        sb.AppendLine(state);
        AppendTerminalStatus(sb);
    }

    private void OnServerTick(float elapsedSeconds)
    {
        if (!IsOverrunning) RefreshEngagedFromClutch();
        RefreshPorts();

        MechanicalNetwork? firstNetwork = firstTerminal.Network;
        MechanicalNetwork? secondNetwork = secondTerminal.Network;
        if (firstNetwork?.Valid == true && secondNetwork?.Valid == true &&
            !ReferenceEquals(firstNetwork, secondNetwork))
        {
            float firstSpeed = firstNetwork.Speed * firstLocalFactor;
            float secondSpeed = secondNetwork.Speed * secondLocalFactor;
            if (IsOverrunning)
            {
                OverrunningCouplingStep step = OverrunningCouplingMath.Solve(
                    firstSpeed, secondSpeed, elapsedSeconds);
                SetAutomaticEngaged(step.Engaged);
                if (step.Changed)
                {
                    firstNetwork.Speed = step.InputSpeed / firstLocalFactor;
                    secondNetwork.Speed = step.OutputSpeed / secondLocalFactor;
                    lastTransfer = step.Transfer;
                    pendingBroadcast = true;
                }
                else
                {
                    lastTransfer = 0;
                }
            }
            else if (engaged)
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
            if (IsOverrunning) SetAutomaticEngaged(false);
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
        if (IsOverrunning) return;
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
        if (Blockentity.Block is BlockOverrunningTransmission transmission)
        {
            return new[] { transmission.InputFace, transmission.InputFace.Opposite };
        }
        return Blockentity.Block.Variant["orientation"] == "we"
            ? new[] { BlockFacing.WEST, BlockFacing.EAST }
            : new[] { BlockFacing.NORTH, BlockFacing.SOUTH };
    }

    internal BlockEntity Owner => Blockentity;

    internal BlockFacing OverrunningInputFace =>
        Blockentity.Block is BlockOverrunningTransmission transmission
            ? transmission.InputFace
            : AxisFaces()[0];

    internal bool TryGetRotorState(out OverrunningRotorState state)
    {
        BlockFacing[] faces = AxisFaces();
        if (!TryReadRotor(faces[0], out float inputAngle, out float inputSpeed) ||
            !TryReadRotor(faces[1], out float outputAngle, out float outputSpeed))
        {
            state = default;
            return false;
        }
        state = new OverrunningRotorState(
            inputAngle, outputAngle, inputSpeed, outputSpeed);
        return true;
    }

    private bool TryReadRotor(BlockFacing face, out float angle, out float speed)
    {
        BEBehaviorMPBase? neighbour = Api?.World.BlockAccessor
            .GetBlockEntity(Position.AddCopy(face))?
            .GetBehavior<BEBehaviorMPBase>();
        if (neighbour?.Network?.Valid != true)
        {
            angle = 0;
            speed = 0;
            return false;
        }
        float factor = neighbour.GearedRatio * (neighbour.IsRotationReversed() ? -1 : 1);
        if (!float.IsFinite(factor) || Math.Abs(factor) < ClutchCouplingMath.SpeedEpsilon)
        {
            factor = 1;
        }
        angle = neighbour.AngleRad;
        speed = neighbour.Network.Speed * factor;
        return true;
    }

    private void SetAutomaticEngaged(bool value)
    {
        if (engaged == value) return;
        engaged = value;
        pendingBroadcast = true;
        Blockentity.MarkDirty(true);
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

    private bool IsOverrunning => Blockentity.Block is BlockOverrunningTransmission;

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
        if (overrunningRenderer != null && Api is ICoreClientAPI capi)
        {
            capi.Event.UnregisterRenderer(overrunningRenderer, EnumRenderStage.Opaque);
            overrunningRenderer.Dispose();
            overrunningRenderer = null;
        }
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

internal readonly record struct OverrunningRotorState(
    float InputAngle,
    float OutputAngle,
    float InputSpeed,
    float OutputSpeed);
