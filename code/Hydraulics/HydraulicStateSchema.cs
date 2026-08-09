using Vintagestory.API.Datastructures;

namespace Gearwright.Hydraulics;

/// <summary>Sequential migrations for the state shared by hydraulic block entities.</summary>
internal static class HydraulicStateSchema
{
    public const int CurrentVersion = 6;

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

        if (schema is 1 or 2 or 3 or 4 or 5)
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
                version = 3;
            }
            if (version == 3)
            {
                // Schema 4 adds the persisted fluid-intake pipe attachment value.
                migrated.SetInt("schemaVersion", 4);
                version = 4;
            }
            if (version == 4)
            {
                // Schema 5 is the explicitly authorized breaking pipe rework. The
                // old bufferless network cache has no physical meaning, while face
                // attachments remain valid and are retained.
                migrated.RemoveAttribute("networkLiquid");
                migrated.RemoveAttribute("networkPressure");
                migrated.RemoveAttribute("networkStatus");
                migrated.RemoveAttribute("networkFlowDirection");
                migrated.RemoveAttribute("lastSettledTotalHours");
                migrated.RemoveAttribute("fractionalItems");
                migrated.SetInt("schemaVersion", 5);
                version = 5;
            }
            if (version == 5)
            {
                // Schema 6 makes every pipe face an explicit persisted port.
                // Old pipes retain their previously automatic free faces.
                for (int face = 0; face < 6; face++)
                {
                    bool free = migrated.GetInt("addon-" + face, 0) == 0;
                    migrated.SetInt("port-" + face, free ? 1 : 0);
                }
                migrated.SetInt("schemaVersion", 6);
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
