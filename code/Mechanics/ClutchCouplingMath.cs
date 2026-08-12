using System;

namespace Gearwright.Mechanics;

public readonly record struct ClutchCouplingStep(
    float FirstSpeed,
    float SecondSpeed,
    float Transfer,
    bool Changed);

/// <summary>Equal-and-opposite speed exchange for the two isolated clutch terminals.</summary>
public static class ClutchCouplingMath
{
    public const float MinimumTransferPerSecond = 0.5f;
    public const float DifferenceTransferGain = 3f;
    public const float MaximumTransferPerSecond = 4f;
    public const float SpeedEpsilon = 0.00001f;

    public static ClutchCouplingStep Solve(
        float firstSpeed,
        float secondSpeed,
        float elapsedSeconds)
    {
        if (!float.IsFinite(firstSpeed) || !float.IsFinite(secondSpeed) ||
            !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
        {
            return new ClutchCouplingStep(firstSpeed, secondSpeed, 0, false);
        }

        float requestedTransfer = (secondSpeed - firstSpeed) * 0.5f;
        if (Math.Abs(requestedTransfer) <= SpeedEpsilon)
        {
            return new ClutchCouplingStep(firstSpeed, secondSpeed, 0, false);
        }

        float difference = Math.Abs(secondSpeed - firstSpeed);
        float transferPerSecond = Math.Clamp(
            MinimumTransferPerSecond + difference * DifferenceTransferGain,
            MinimumTransferPerSecond,
            MaximumTransferPerSecond);
        float limit = transferPerSecond * Math.Min(elapsedSeconds, 0.25f);
        float transfer = Math.Clamp(requestedTransfer, -limit, limit);
        return new ClutchCouplingStep(
            firstSpeed + transfer,
            secondSpeed - transfer,
            transfer,
            true);
    }
}
