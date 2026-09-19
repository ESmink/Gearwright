using Vintagestory.API.MathTools;

namespace Gearwright.Mechanics;

/// <summary>A radial device using the shared journal and guided crosshead.</summary>
public interface IReciprocatingDriveDevice
{
    BlockPos Position { get; }
    BlockFacing DriveFace { get; }
    BlockFacing ShaftFace { get; }

    /// <summary>Finite opposing load, excluding the drive shaft's own bearing friction.</summary>
    float SampleReciprocatingLoad(double angle, double travel, double seconds);
    /// <summary>Signed torque in the device's angle frame, evaluated at the proposed end of the step.</summary>
    float SampleReciprocatingTorque(double angle, double travel, double seconds) => 0;
    void StepReciprocatingDrive(double seconds);
}
