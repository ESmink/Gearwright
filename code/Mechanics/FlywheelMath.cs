using System;

namespace Gearwright.Mechanics;

/// <summary>Deterministic momentum exchange used by the vanilla mechanical network node.</summary>
public static class FlywheelMath
{
    public const float MaxSupportedSpeed = 0.6f;
    public const float BaseBearingResistance = 0.0008f;
    public const float MaxTransferTorque = 0.18f;
    public const float Coupling = 2.5f;
    public const float Inertia = 12f;
    public const float ExchangeSeconds = 0.25f;
    public const float BearingDragTorque = 0.00025f;

    public static FlywheelExchange Exchange(float storedSpeed, float networkSpeed)
    {
        storedSpeed = SanitizeSpeed(storedSpeed);
        networkSpeed = SanitizeSpeed(networkSpeed);
        float difference = networkSpeed - storedSpeed;
        float transfer = Math.Min(MaxTransferTorque, Math.Abs(difference) * Coupling);
        bool sameDirection = Math.Abs(storedSpeed) < 0.000001f ||
                             Math.Abs(networkSpeed) < 0.000001f ||
                             Math.Sign(storedSpeed) == Math.Sign(networkSpeed);
        bool networkIsDriving = sameDirection && Math.Abs(networkSpeed) > Math.Abs(storedSpeed);

        float torque = networkIsDriving ? 0 : Math.Sign(storedSpeed) * transfer;
        float resistance = BaseBearingResistance + (networkIsDriving ? transfer : 0);
        float speedStep = transfer * ExchangeSeconds / Inertia;
        float nextStored = MoveTowards(storedSpeed, networkSpeed, speedStep);
        nextStored = MoveTowards(
            nextStored,
            0,
            BearingDragTorque * ExchangeSeconds / Inertia);
        return new FlywheelExchange(SanitizeSpeed(nextStored), torque, resistance);
    }

    public static float SanitizeSpeed(float speed)
    {
        if (!float.IsFinite(speed)) return 0;
        return Math.Clamp(speed, -MaxSupportedSpeed, MaxSupportedSpeed);
    }

    public static float RevolutionsPerMinute(float speed) =>
        Math.Abs(SanitizeSpeed(speed)) * 50f * 60f / (2f * MathF.PI);

    private static float MoveTowards(float current, float target, float maxDelta)
    {
        float delta = target - current;
        if (Math.Abs(delta) <= maxDelta) return target;
        return current + Math.Sign(delta) * maxDelta;
    }
}

public readonly record struct FlywheelExchange(
    float StoredSpeed,
    float Torque,
    float Resistance);
