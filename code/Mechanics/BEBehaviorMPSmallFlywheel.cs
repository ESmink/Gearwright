using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>Vanilla mechanical node that stores and returns angular momentum.</summary>
public sealed class BEBehaviorMPSmallFlywheel : BEBehaviorMPBase
{
    private const string StorageKey = "gearwrightFlywheel";
    private ITreeAttribute preservedState = new TreeAttribute();
    private bool canWrite = true;
    private float storedSpeed;
    private long lastExchangeTick = long.MinValue;
    private FlywheelExchange lastExchange;
    private long lastDirtyTick;
    private long networkMaintenanceListenerId;

    public BEBehaviorMPSmallFlywheel(BlockEntity blockentity) : base(blockentity) { }

    public float StoredSpeed => storedSpeed;
    public bool CanWriteState => canWrite;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api.Side == EnumAppSide.Server)
        {
            networkMaintenanceListenerId = Blockentity.RegisterGameTickListener(
                MaintainNetwork,
                200);
        }
    }

    protected override CompositeShape GetShape()
    {
        bool alongX = Blockentity.Block.Variant["rotation"] == "we";
        return new CompositeShape
        {
            Base = new AssetLocation("gearwright", "block/small-flywheel-wheel"),
            rotateY = alongX ? 90 : 0
        };
    }

    public override void SetOrientations()
    {
        AxisSign = Blockentity.Block.Variant["rotation"] == "we"
            ? new[] { -1, 0, 0 }
            : new[] { 0, 0, -1 };
    }

    public override float GetResistance() => FlywheelMath.BaseBearingResistance;

    public override float GetTorque(long tick, float speed, out float resistance)
    {
        if (!canWrite)
        {
            resistance = FlywheelMath.BaseBearingResistance;
            return 0;
        }
        if (tick == lastExchangeTick)
        {
            resistance = lastExchange.Resistance;
            return lastExchange.Torque;
        }

        float previous = storedSpeed;
        lastExchange = FlywheelMath.Exchange(storedSpeed, speed);
        storedSpeed = lastExchange.StoredSpeed;
        lastExchangeTick = tick;
        resistance = lastExchange.Resistance;
        if (Api?.Side == EnumAppSide.Server &&
            Math.Abs(storedSpeed - previous) >= 0.00002f && tick - lastDirtyTick >= 40)
        {
            lastDirtyTick = tick;
            Blockentity.MarkDirty(false);
        }
        return lastExchange.Torque;
    }

    public override bool OnTesselation(
        ITerrainMeshPool mesher,
        ITesselatorAPI tesselator)
    {
        base.OnTesselation(mesher, tesselator);
        Vintagestory.API.Common.Shape frame = Vintagestory.API.Common.Shape.TryGet(
            Api, "gearwright:shapes/block/small-flywheel-frame.json");
        tesselator.TesselateShape(Blockentity.Block, frame, out MeshData mesh, new Vec3f());
        if (Blockentity.Block.Variant["rotation"] == "we")
        {
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, GameMath.PIHALF, 0);
        }
        mesher.AddMeshData(mesh, 1);
        return true;
    }

    public override void FromTreeAttributes(
        ITreeAttribute tree,
        IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        ITreeAttribute? state = tree.GetTreeAttribute(StorageKey);
        if (state == null)
        {
            preservedState = new TreeAttribute();
            canWrite = true;
            storedSpeed = 0;
            return;
        }

        int? schema = state.TryGetInt("schemaVersion");
        preservedState = FlywheelStateSchema.PrepareForRead(
            state, out canWrite, out _);
        storedSpeed = FlywheelMath.SanitizeSpeed((float)preservedState.GetDouble("storedSpeed", 0));
        if (!canWrite)
        {
            worldAccessForResolve.Logger.Error(
                "[Gearwright] Flywheel state at {0} has an unsupported schema ({1}). The original data will remain read-only and inertia is disabled.",
                Position, schema?.ToString() ?? "non-integer");
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (!canWrite)
        {
            tree[StorageKey] = preservedState.Clone();
            return;
        }

        ITreeAttribute state = preservedState.Clone();
        state.SetInt("schemaVersion", FlywheelStateSchema.CurrentVersion);
        state.SetDouble("storedSpeed", storedSpeed);
        preservedState = state.Clone();
        tree[StorageKey] = state;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);
        sb.AppendLine(Lang.Get(
            "gearwright:flywheel-stored-speed",
            FlywheelMath.RevolutionsPerMinute(storedSpeed)));
        if (!canWrite) sb.AppendLine(Lang.Get("gearwright:flywheel-state-read-only"));
    }

    public override void OnBlockRemoved()
    {
        StopNetworkMaintenance();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        StopNetworkMaintenance();
        base.OnBlockUnloaded();
    }

    private void MaintainNetwork(float elapsedSeconds)
    {
        List<ConnectedNeighbour> connected = ConnectedNeighbours();
        bool hasValidNetwork = Network?.Valid == true;
        ConnectedNeighbour? mismatch = null;
        foreach (ConnectedNeighbour neighbour in connected)
        {
            if (neighbour.Behaviour.Network?.Valid != true ||
                !ReferenceEquals(Network, neighbour.Behaviour.Network))
            {
                mismatch = neighbour;
                break;
            }
        }

        FlywheelNetworkAction action = FlywheelNetworkPlan.Decide(
            hasValidNetwork,
            connected.Count,
            mismatch != null);
        switch (action)
        {
            case FlywheelNetworkAction.CreateStandalone:
                LeaveInvalidNetwork();
                MechanicalNetwork standalone = manager.CreateNetwork(this);
                JoinNetwork(standalone);
                standalone.Speed = storedSpeed;
                standalone.broadcastData();
                break;

            case FlywheelNetworkAction.DiscoverFromNeighbour:
                LeaveInvalidNetwork();
                ConnectedNeighbour seed = connected.Find(
                    neighbour => neighbour.Behaviour.Network?.Valid == true) ?? connected[0];
                bool neighbourAlreadyRunning = seed.Behaviour.Network?.Valid == true;
                MechanicalNetwork? discovered = CreateJoinAndDiscoverNetwork(seed.Face);
                if (!neighbourAlreadyRunning && discovered?.Valid == true)
                {
                    discovered.Speed = storedSpeed;
                    discovered.broadcastData();
                }
                break;

            case FlywheelNetworkAction.ConnectNeighbour:
                tryConnect(mismatch!.Face);
                break;
        }
    }

    private List<ConnectedNeighbour> ConnectedNeighbours()
    {
        List<ConnectedNeighbour> connected = new();
        foreach (BlockFacing face in AxisFaces())
        {
            BlockPos neighbourPosition = Position.AddCopy(face);
            Block block = Api.World.BlockAccessor.GetBlock(neighbourPosition);
            if (block is not IMechanicalPowerBlock mechanicalBlock ||
                !mechanicalBlock.HasMechPowerConnectorAt(
                    Api.World,
                    neighbourPosition,
                    face.Opposite,
                    (BlockMPBase)Blockentity.Block))
            {
                continue;
            }

            BEBehaviorMPBase? behaviour = Api.World.BlockAccessor
                .GetBlockEntity(neighbourPosition)?
                .GetBehavior<BEBehaviorMPBase>();
            if (behaviour != null) connected.Add(new ConnectedNeighbour(face, behaviour));
        }
        return connected;
    }

    private BlockFacing[] AxisFaces() =>
        Blockentity.Block.Variant["rotation"] == "we"
            ? new[] { BlockFacing.WEST, BlockFacing.EAST }
            : new[] { BlockFacing.NORTH, BlockFacing.SOUTH };

    private void LeaveInvalidNetwork()
    {
        if (Network != null) LeaveNetwork();
    }

    private void StopNetworkMaintenance()
    {
        if (networkMaintenanceListenerId == 0) return;
        Blockentity.UnregisterGameTickListener(networkMaintenanceListenerId);
        networkMaintenanceListenerId = 0;
    }

    private sealed record ConnectedNeighbour(
        BlockFacing Face,
        BEBehaviorMPBase Behaviour);
}
