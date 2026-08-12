using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace Gearwright.Mechanics;

/// <summary>A zero-loss endpoint which belongs to one side's Vanilla network.</summary>
internal sealed class ClutchTerminal : IMechanicalPowerNode
{
    private readonly BlockEntity owner;

    public ClutchTerminal(BlockEntity owner)
    {
        this.owner = owner;
    }

    public MechanicalNetwork? Network { get; private set; }
    public float GearedRatio { get; set; } = 1;
    public float OverheatValue { get; set; }

    public BlockPos GetPosition() => owner.Pos;

    public float GetTorque(long tick, float speed, out float resistance)
    {
        resistance = 0;
        return 0;
    }

    public bool Attach(MechanicalNetwork desired)
    {
        if (ReferenceEquals(Network, desired) && desired.Valid) return true;
        LeaveNetwork();
        if (!desired.Valid) return false;

        BlockPos position = GetPosition();
        if (desired.nodes.TryGetValue(position, out IMechanicalPowerNode? occupant) &&
            !ReferenceEquals(occupant, this))
        {
            return false;
        }

        Network = desired;
        desired.Join(this);
        return desired.nodes.TryGetValue(position, out occupant) && ReferenceEquals(occupant, this);
    }

    public void LeaveNetwork()
    {
        MechanicalNetwork? previous = Network;
        Network = null;
        BlockPos? position = owner.Pos;
        if (previous != null &&
            position != null &&
            previous.nodes.TryGetValue(position, out IMechanicalPowerNode? occupant) &&
            ReferenceEquals(occupant, this))
        {
            previous.Leave(this);
        }
    }
}
