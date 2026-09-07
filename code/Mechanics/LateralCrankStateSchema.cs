using Vintagestory.API.Datastructures;

namespace Gearwright.Mechanics;

/// <summary>Version gate for each crank's independently adjustable journal angle.</summary>
public static class LateralCrankStateSchema
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
