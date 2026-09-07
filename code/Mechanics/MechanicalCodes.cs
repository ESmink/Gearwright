namespace Gearwright.Mechanics;

public static class MechanicalCodes
{
    public const string LateralCrankBlockCode = "lateral-crank";
    public const string FlywheelBlockClass = "GearwrightSmallFlywheel";
    public const string FlywheelBehaviorClass = "GearwrightMPSmallFlywheel";
    public const string ControlledTransmissionBlockClass = "GearwrightControlledTransmission";
    public const string ControlledTransmissionBehaviorClass = "GearwrightMPControlledTransmission";
    public const string ControlledClutchBlockClass = "GearwrightControlledClutch";
    public const string ControlledClutchEntityClass = "GearwrightControlledClutch";
    public const string OverrunningTransmissionBlockClass = "GearwrightOverrunningTransmission";
    public const string OverrunningTransmissionBehaviorClass = "GearwrightMPOverrunningTransmission";
    public const string LateralCrankBlockClass = "GearwrightLateralCrank";
    public const string LateralCrankBehaviorClass = "GearwrightMPLateralCrank";

    public static string LateralCrankVariantPath(string rotation) => rotation switch
    {
        "ns" or "we" => $"{LateralCrankBlockCode}-{rotation}",
        _ => throw new System.ArgumentOutOfRangeException(
            nameof(rotation), rotation, "A reciprocating drive shaft must use rotation 'ns' or 'we'.")
    };
}
