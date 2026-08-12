namespace Gearwright.Mechanics;

public enum FlywheelNetworkAction
{
    None,
    CreateStandalone,
    DiscoverFromNeighbour,
    ConnectNeighbour
}

/// <summary>Pure lifecycle decision used by the server-side flywheel network keeper.</summary>
public static class FlywheelNetworkPlan
{
    public static FlywheelNetworkAction Decide(
        bool hasValidNetwork,
        int connectedNeighbourCount,
        bool hasDifferentOrMissingNeighbourNetwork)
    {
        if (!hasValidNetwork)
        {
            return connectedNeighbourCount > 0
                ? FlywheelNetworkAction.DiscoverFromNeighbour
                : FlywheelNetworkAction.CreateStandalone;
        }

        return hasDifferentOrMissingNeighbourNetwork
            ? FlywheelNetworkAction.ConnectNeighbour
            : FlywheelNetworkAction.None;
    }
}
