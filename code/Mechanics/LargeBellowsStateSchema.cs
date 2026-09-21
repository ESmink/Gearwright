using System;
using Vintagestory.API.Datastructures;

namespace Gearwright.Mechanics;

internal static class LargeBellowsStateSchema
{
    public const int CurrentVersion = 1;
    public const double Capacity = .4;
    public static ITreeAttribute Read(ITreeAttribute original, out bool writable, out string? problem)
    {
        int? version = original["schemaVersion"] is IntAttribute ? original.TryGetInt("schemaVersion") : null;
        double air = original["air"] is DoubleAttribute ? original.GetDouble("air") : 0;
        writable = version == CurrentVersion && double.IsFinite(air) && air >= 0 && air <= Capacity &&
            (!original.HasAttribute("air") || original["air"] is DoubleAttribute);
        problem = writable ? null : version > CurrentVersion ? "newer" : "invalid";
        return original.Clone();
    }
}
