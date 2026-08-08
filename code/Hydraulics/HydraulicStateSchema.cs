using Vintagestory.API.Datastructures;

namespace Gearwright.Hydraulics;

/// <summary>Sequential migrations for the state shared by hydraulic block entities.</summary>
internal static class HydraulicStateSchema
{
    public const int CurrentVersion = 3;

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

        if (schema is 1 or 2)
        {
            ITreeAttribute migrated = original.Clone();
            int version = schema.Value;
            if (version == 1)
            {
                // Schema 2 added directional gravity-drain placement state.
                migrated.SetInt("schemaVersion", 2);
                version = 2;
            }
            if (version == 2)
            {
                // Schema 3 adds a derived, optional network-flow direction cache.
                migrated.SetInt("schemaVersion", 3);
            }
            canWrite = true;
            problem = null;
            return migrated;
        }

        canWrite = false;
        problem = schema == null ? "invalid" : schema > CurrentVersion ? "newer" : "unsupported";
        return original.Clone();
    }
}
