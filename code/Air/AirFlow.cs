using System;
using Vintagestory.API.MathTools;

namespace Gearwright.Air;

/// <summary>
/// Air arriving during one simulation step. Rate is in vanilla bellows air units
/// per real second; Seconds is the simulated duration, and Direction is travel
/// from source to receiver. This is free airflow, not a pressure or pipe model.
/// </summary>
public readonly record struct AirFlow(double Rate, double Seconds, BlockFacing Direction)
{
    public double Amount => Rate * Seconds;
    public bool IsValid => Direction != null && double.IsFinite(Rate) && Rate > 0 &&
        double.IsFinite(Seconds) && Seconds > 0 && double.IsFinite(Amount) && Amount <= float.MaxValue;
}
