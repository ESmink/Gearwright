using System;

namespace Gearwright.Mechanics;

public readonly record struct OverrunningCouplingStep(
    float InputSpeed,
    float OutputSpeed,
    float Transfer,
    bool Engaged,
    bool Changed);

public readonly record struct OverrunningLockState(
    bool Initialized,
    bool Engaged,
    float Direction,
    float LastDirectedRelativeAngle,
    float PhaseError);

public readonly record struct OverrunningCouplingUpdate(
    OverrunningLockState State,
    OverrunningCouplingStep Step,
    float ContactThreat);

/// <summary>Hysteretic one-way policy around the bounded controlled-clutch solver.</summary>
public static class OverrunningCouplingMath
{
    public const float EngagementEpsilon = 0.00001f;
    public const float EngageSpeedDifference = .001f;
    public const float ReleaseSpeedDifference = .012f;
    public const float MaximumPhaseRecoverySpeed = .08f;
    public const float MaximumContactTorqueMultiplier = 4f;

    /// <summary>
    /// Chooses the ratchet's live handedness. A moving input establishes the
    /// intended drive direction; a moving output supplies it while the input is
    /// stopped and freewheeling.
    /// </summary>
    public static float OperatingDirection(float inputSpeed, float outputSpeed)
    {
        if (float.IsFinite(inputSpeed) && Math.Abs(inputSpeed) > EngagementEpsilon)
        {
            return Math.Sign(inputSpeed);
        }
        if (float.IsFinite(outputSpeed) && Math.Abs(outputSpeed) > EngagementEpsilon)
        {
            return Math.Sign(outputSpeed);
        }
        return 1;
    }

    public static OverrunningCouplingStep Solve(
        float inputSpeed,
        float outputSpeed,
        float elapsedSeconds)
    {
        return Solve(inputSpeed, outputSpeed, elapsedSeconds, false, 0);
    }

    public static OverrunningCouplingStep Solve(
        float inputSpeed,
        float outputSpeed,
        float elapsedSeconds,
        bool wasEngaged,
        float phaseError)
    {
        if (!float.IsFinite(inputSpeed) || !float.IsFinite(outputSpeed) ||
            !float.IsFinite(elapsedSeconds) || !float.IsFinite(phaseError) ||
            elapsedSeconds <= 0)
        {
            return new OverrunningCouplingStep(
                inputSpeed, outputSpeed, 0, false, false);
        }

        float direction = OperatingDirection(inputSpeed, outputSpeed);
        float directedInput = inputSpeed * direction;
        float directedOutput = outputSpeed * direction;
        float inputLead = directedInput - directedOutput;
        float contactThreat = OverrunningPawlMath.ContactThreat(phaseError);
        float recoveryOutputLead = contactThreat * MaximumPhaseRecoverySpeed;
        float releaseDifference = ReleaseSpeedDifference + recoveryOutputLead;
        bool engaged = wasEngaged
            ? -inputLead <= releaseDifference
            : inputLead >= EngageSpeedDifference;
        if (!engaged)
        {
            return new OverrunningCouplingStep(
                inputSpeed, outputSpeed, 0, false, false);
        }

        // A loaded locking face behaves like a stiff angular spring. Offset the
        // virtual speeds so the ordinary clutch exchange briefly lets the
        // output catch up to the tooth, while the applied changes remain equal
        // and opposite on the two real networks.
        float virtualInput = directedInput + recoveryOutputLead * .5f;
        float virtualOutput = directedOutput - recoveryOutputLead * .5f;
        float contactMultiplier = 1 +
            (MaximumContactTorqueMultiplier - 1) * contactThreat;
        ClutchCouplingStep step = ClutchCouplingMath.Solve(
            virtualInput,
            virtualOutput,
            elapsedSeconds * contactMultiplier);
        float transfer = step.Transfer;
        return new OverrunningCouplingStep(
            (directedInput + transfer) * direction,
            (directedOutput - transfer) * direction,
            transfer * direction,
            true,
            step.Changed);
    }

    public static OverrunningCouplingUpdate Advance(
        OverrunningLockState state,
        float inputSpeed,
        float outputSpeed,
        float relativeAngle,
        float elapsedSeconds)
    {
        if (!float.IsFinite(inputSpeed) || !float.IsFinite(outputSpeed) ||
            !float.IsFinite(relativeAngle) || !float.IsFinite(elapsedSeconds) ||
            elapsedSeconds <= 0)
        {
            OverrunningCouplingStep rejected = new(
                inputSpeed, outputSpeed, 0, false, false);
            return new OverrunningCouplingUpdate(default, rejected, 0);
        }

        float direction = OperatingDirection(inputSpeed, outputSpeed);
        float directedRelative = relativeAngle * direction;
        bool directionChanged = !state.Initialized || state.Direction != direction;
        bool wasEngaged = state.Engaged && !directionChanged;
        float phaseError = wasEngaged
            ? Math.Clamp(
                state.PhaseError + SignedAngleDelta(
                    directedRelative,
                    state.LastDirectedRelativeAngle),
                -OverrunningPawlMath.ToothPitch,
                OverrunningPawlMath.ToothPitch)
            : 0;

        OverrunningCouplingStep step = Solve(
            inputSpeed,
            outputSpeed,
            elapsedSeconds,
            wasEngaged,
            phaseError);
        if (!step.Engaged || !wasEngaged) phaseError = 0;
        OverrunningLockState next = new(
            true,
            step.Engaged,
            direction,
            directedRelative,
            phaseError);
        return new OverrunningCouplingUpdate(
            next,
            step,
            OverrunningPawlMath.ContactThreat(phaseError));
    }

    private static float SignedAngleDelta(float current, float previous)
    {
        float period = 2 * MathF.PI;
        float delta = (current - previous + MathF.PI) % period;
        if (delta < 0) delta += period;
        return delta - MathF.PI;
    }
}

/// <summary>Pure live tooth-phase mapping shared by rendering and contracts.</summary>
public static class OverrunningPawlMath
{
    public const int ToothCount = 15;
    public const float ToothPitch = 2 * MathF.PI / ToothCount;
    public const float MaximumLift = 30 * MathF.PI / 180;
    public const float SpringLiftFraction = .35f;
    public const float FullContactThreatPhaseLag = ToothPitch * .12f;

    private const float RampStart = .30f;
    private const float RampFull = .76f;
    private const float DropStart = .985f;

    public static float Lift(float directedRelativeAngle)
    {
        if (!float.IsFinite(directedRelativeAngle)) return 0;

        float cycle = PositiveModulo(directedRelativeAngle / ToothPitch, 1);
        if (cycle < RampStart) return 0;
        if (cycle < RampFull)
        {
            float progress = (cycle - RampStart) / (RampFull - RampStart);
            return MaximumLift * MathF.Pow(progress, 1.5f);
        }
        if (cycle < DropStart) return MaximumLift;
        return MaximumLift * (1 - cycle) / (1 - DropStart);
    }

    public static float ContactThreat(float phaseError)
    {
        if (!float.IsFinite(phaseError) || phaseError >= 0) return 0;
        return Math.Clamp(-phaseError / FullContactThreatPhaseLag, 0, 1);
    }

    private static float PositiveModulo(float value, float divisor)
    {
        float result = value % divisor;
        return result < 0 ? result + divisor : result;
    }
}
