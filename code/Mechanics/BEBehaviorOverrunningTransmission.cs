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
/// Couples two permanent Vanilla mechanical networks in one direction without
/// inheriting the Vanilla transmission behavior or renderer.
/// </summary>
public sealed class BEBehaviorMPOverrunningTransmission : BlockEntityBehavior
{
    private const int SolverIntervalMilliseconds = 50;
    private const long BroadcastIntervalMilliseconds = 100;

    private readonly ClutchTerminal inputTerminal;
    private readonly ClutchTerminal outputTerminal;
    private long solverListenerId;
    private long lastBroadcastMilliseconds;
    private bool pendingBroadcast;
    private bool bypassed;
    private float inputLocalFactor = 1;
    private float outputLocalFactor = 1;
    private OverrunningLockState lockState;
    private MechanicalNetwork? lockedInputNetwork;
    private MechanicalNetwork? lockedOutputNetwork;
    private OverrunningTransmissionRenderer? renderer;

    public BEBehaviorMPOverrunningTransmission(BlockEntity blockentity) : base(blockentity)
    {
        inputTerminal = new ClutchTerminal(blockentity);
        outputTerminal = new ClutchTerminal(blockentity);
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api.Side == EnumAppSide.Server)
        {
            // Adjacent block entities may not have initialized their Vanilla
            // mechanical managers yet, so discovery starts on the first tick.
            solverListenerId = Blockentity.RegisterGameTickListener(
                OnServerTick,
                SolverIntervalMilliseconds);
        }
        else if (api is ICoreClientAPI capi)
        {
            renderer = new OverrunningTransmissionRenderer(this, capi);
            capi.Event.RegisterRenderer(
                renderer,
                EnumRenderStage.Opaque,
                "gearwright-overrunning-transmission");
        }
    }

    public void RefreshNow()
    {
        if (Api?.Side == EnumAppSide.Server) RefreshPorts();
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
        sb.AppendLine(Lang.Get(
            "gearwright:overrunning-input-face",
            Lang.Get("direction-" + InputFace.Code)));

        if (!TryGetRotorState(out OverrunningRotorState state))
        {
            sb.AppendLine(Lang.Get("gearwright:overrunning-freewheeling"));
            sb.AppendLine(Lang.Get("gearwright:controlled-clutch-waiting"));
            return;
        }

        if (PortsShareNetwork())
        {
            sb.AppendLine(Lang.Get("gearwright:overrunning-freewheeling"));
            sb.AppendLine(Lang.Get("gearwright:controlled-clutch-bypassed"));
            return;
        }

        float direction = OverrunningCouplingMath.OperatingDirection(
            state.InputSpeed,
            state.OutputSpeed);
        bool driving = (state.InputSpeed - state.OutputSpeed) * direction >
            OverrunningCouplingMath.EngagementEpsilon;
        sb.AppendLine(driving
            ? Lang.Get("gearwright:overrunning-engaged")
            : Lang.Get("gearwright:overrunning-freewheeling"));
    }

    private void OnServerTick(float elapsedSeconds)
    {
        RefreshPorts();
        MechanicalNetwork? inputNetwork = inputTerminal.Network;
        MechanicalNetwork? outputNetwork = outputTerminal.Network;
        if (bypassed ||
            inputNetwork?.Valid != true ||
            outputNetwork?.Valid != true ||
            ReferenceEquals(inputNetwork, outputNetwork))
        {
            ResetLockState();
            BroadcastIfDue();
            return;
        }

        if (!ReferenceEquals(inputNetwork, lockedInputNetwork) ||
            !ReferenceEquals(outputNetwork, lockedOutputNetwork))
        {
            lockState = default;
            lockedInputNetwork = inputNetwork;
            lockedOutputNetwork = outputNetwork;
        }

        float inputSpeed = inputNetwork.Speed * inputLocalFactor;
        float outputSpeed = outputNetwork.Speed * outputLocalFactor;
        float relativeAngle =
            LocalAngle(outputNetwork, outputLocalFactor) -
            LocalAngle(inputNetwork, inputLocalFactor);
        OverrunningCouplingUpdate update = OverrunningCouplingMath.Advance(
            lockState,
            inputSpeed,
            outputSpeed,
            relativeAngle,
            elapsedSeconds);
        lockState = update.State;
        OverrunningCouplingStep step = update.Step;
        if (!step.Changed)
        {
            BroadcastIfDue();
            return;
        }

        inputNetwork.Speed = step.InputSpeed / inputLocalFactor;
        outputNetwork.Speed = step.OutputSpeed / outputLocalFactor;
        pendingBroadcast = true;
        BroadcastIfDue();
    }

    private void RefreshPorts()
    {
        PortCandidate input = ResolvePort(InputFace);
        PortCandidate output = ResolvePort(InputFace.Opposite);
        bypassed = input.Network != null && ReferenceEquals(input.Network, output.Network);

        if (bypassed)
        {
            // A MechanicalNetwork is keyed by position, so a single terminal
            // represents this block when another route already joins both ends.
            inputTerminal.Attach(input.Network!);
            outputTerminal.LeaveNetwork();
        }
        else
        {
            AttachOrLeave(inputTerminal, input.Network);
            AttachOrLeave(outputTerminal, output.Network);
        }

        inputLocalFactor = input.LocalFactor;
        outputLocalFactor = output.LocalFactor;
    }

    private PortCandidate ResolvePort(BlockFacing face)
    {
        BEBehaviorMPBase? neighbour = GetNeighbour(face);
        if (neighbour == null) return default;

        MechanicalNetwork? network = neighbour.Network;
        if (network?.Valid != true)
        {
            // The boundary is invisible to Vanilla discovery. Bootstrap a lone
            // adjacent shaft on its own side instead of discovering through us.
            network = neighbour.CreateJoinAndDiscoverNetwork(face.Opposite);
        }
        if (network?.Valid != true) return default;

        return new PortCandidate(network, LocalFactor(neighbour));
    }

    private BEBehaviorMPBase? GetNeighbour(BlockFacing face)
    {
        BlockPos neighbourPosition = Blockentity.Pos.AddCopy(face);
        Block block = Api.World.BlockAccessor.GetBlock(neighbourPosition);
        if (block is not BlockMPBase mechanicalBlock ||
            !mechanicalBlock.HasMechPowerConnectorAt(
                Api.World,
                neighbourPosition,
                face.Opposite,
                (BlockMPBase)Blockentity.Block))
        {
            return null;
        }

        return Api.World.BlockAccessor
            .GetBlockEntity(neighbourPosition)?
            .GetBehavior<BEBehaviorMPBase>();
    }

    internal BlockEntity Owner => Blockentity;

    internal BlockFacing InputFace =>
        ((BlockOverrunningTransmission)Blockentity.Block).InputFace;

    internal bool TryGetRotorState(out OverrunningRotorState state)
    {
        BEBehaviorMPBase? input = GetNeighbour(InputFace);
        BEBehaviorMPBase? output = GetNeighbour(InputFace.Opposite);
        if (input?.Network?.Valid != true || output?.Network?.Valid != true)
        {
            state = default;
            return false;
        }

        float inputFactor = LocalFactor(input);
        float outputFactor = LocalFactor(output);
        state = new OverrunningRotorState(
            LocalAngle(input.Network, inputFactor),
            LocalAngle(output.Network, outputFactor),
            SignedLocalSpeed(input.Network, inputFactor),
            SignedLocalSpeed(output.Network, outputFactor));
        return true;
    }

    private float SignedLocalSpeed(MechanicalNetwork network, float localFactor)
    {
        float speed = network.Speed;
        if (Api.Side == EnumAppSide.Client)
        {
            // Vanilla packets store client speed as an absolute magnitude and
            // carry its sign separately. Mirror MechanicalNetwork.ClientTick
            // so overrun detection agrees with the angles players actually see.
            bool forward =
                (network.TurnDir == EnumRotDirection.Clockwise) ^
                network.DirectionHasReversed;
            speed = Math.Abs(speed) * (forward ? 1 : -1);
        }
        return speed * localFactor;
    }

    private static float LocalAngle(MechanicalNetwork network, float localFactor)
    {
        float period = GameMath.PI * 2;
        float angle = network.AngleRad * localFactor % period;
        return angle < 0 ? angle + period : angle;
    }

    private bool PortsShareNetwork()
    {
        MechanicalNetwork? input = GetNeighbour(InputFace)?.Network;
        MechanicalNetwork? output = GetNeighbour(InputFace.Opposite)?.Network;
        return input?.Valid == true && ReferenceEquals(input, output);
    }

    private static float LocalFactor(BEBehaviorMPBase neighbour)
    {
        float factor = neighbour.GearedRatio * (neighbour.IsRotationReversed() ? -1 : 1);
        return !float.IsFinite(factor) || Math.Abs(factor) < ClutchCouplingMath.SpeedEpsilon
            ? 1
            : factor;
    }

    private void AttachOrLeave(ClutchTerminal terminal, MechanicalNetwork? network)
    {
        if (network == null)
        {
            terminal.LeaveNetwork();
            return;
        }
        terminal.Attach(network);
    }

    private void BroadcastIfDue()
    {
        if (!pendingBroadcast) return;
        long now = Api.World.ElapsedMilliseconds;
        if (now - lastBroadcastMilliseconds < BroadcastIntervalMilliseconds) return;

        MechanicalNetwork? input = inputTerminal.Network;
        MechanicalNetwork? output = outputTerminal.Network;
        input?.broadcastData();
        if (output != null && !ReferenceEquals(input, output)) output.broadcastData();
        lastBroadcastMilliseconds = now;
        pendingBroadcast = false;
    }

    private void ResetLockState()
    {
        lockState = default;
        lockedInputNetwork = null;
        lockedOutputNetwork = null;
    }

    private void Shutdown()
    {
        if (renderer != null && Api is ICoreClientAPI capi)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            renderer.Dispose();
            renderer = null;
        }
        if (solverListenerId != 0)
        {
            Blockentity.UnregisterGameTickListener(solverListenerId);
            solverListenerId = 0;
        }
        inputTerminal.LeaveNetwork();
        outputTerminal.LeaveNetwork();
        ResetLockState();
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
