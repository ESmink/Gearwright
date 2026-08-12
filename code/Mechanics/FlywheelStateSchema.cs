using Vintagestory.API.Datastructures;

namespace Gearwright.Mechanics;

/// <summary>Version gate for persisted flywheel momentum.</summary>
public static class FlywheelStateSchema
{
    public const int CurrentVersion = 1;

    public static ITreeAttribute PrepareForRead(
        ITreeAttribute original,
        out bool canWrite,
        out string? problem)
    {
        int? schema = original.TryGetInt("schemaVersion");
        if (schema == CurrentVersion)
        {
            canWrite = true;
            problem = null;
            return original.Clone();
        }

        canWrite = false;
        problem = schema == null ? "invalid" : schema > CurrentVersion ? "newer" : "unsupported";
        return original.Clone();
    }
}
