using System;

namespace Gearwright.Mechanics;

public readonly record struct OverrunningCouplingStep(
    float InputSpeed,
    float OutputSpeed,
    float Transfer,
    bool Engaged,
    bool Changed);

/// <summary>One-way policy wrapped around the bounded controlled-clutch solver.</summary>
public static class OverrunningCouplingMath
{
    public const float EngagementEpsilon = 0.00001f;

    public static OverrunningCouplingStep Solve(
        float inputSpeed,
        float outputSpeed,
        float elapsedSeconds)
    {
        if (!float.IsFinite(inputSpeed) || !float.IsFinite(outputSpeed) ||
            !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0 ||
            inputSpeed - outputSpeed <= EngagementEpsilon)
        {
            return new OverrunningCouplingStep(
                inputSpeed, outputSpeed, 0, false, false);
        }

        ClutchCouplingStep step = ClutchCouplingMath.Solve(
            inputSpeed, outputSpeed, elapsedSeconds);
        return new OverrunningCouplingStep(
            step.FirstSpeed,
            step.SecondSpeed,
            step.Transfer,
            true,
            step.Changed);
    }
}
