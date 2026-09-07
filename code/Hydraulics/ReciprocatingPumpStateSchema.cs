using Vintagestory.API.Datastructures;

namespace Gearwright.Hydraulics;

/// <summary>Version gate for the pump's chamber and placement state.</summary>
public static class ReciprocatingPumpStateSchema
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
