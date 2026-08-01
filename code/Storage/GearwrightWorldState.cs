using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Gearwright.Storage;

/// <summary>
/// A compatibility-first wrapper around the mod's world-level state. The underlying JSON
/// object is retained so fields added by newer builds are not discarded by an older reader.
/// </summary>
internal sealed class GearwrightWorldState
{
    public const int CurrentSchemaVersion = 1;

    private const string FormatName = "gearwright-world-state";
    private readonly JObject document;

    public int SchemaVersion => TryReadSchemaVersion(document, out int version) ? version : -1;
    public string CreatedWith => document.Value<string>("createdWith") ?? "unknown";
    public bool CanWrite { get; private set; }
    public string Notice { get; private set; }

    private GearwrightWorldState(JObject document, bool canWrite, string notice)
    {
        this.document = document;
        CanWrite = canWrite;
        Notice = notice;
    }

    public static GearwrightWorldState Create(string modVersion)
    {
        JObject document = new()
        {
            ["format"] = FormatName,
            ["schemaVersion"] = CurrentSchemaVersion,
            ["createdWith"] = modVersion,
            ["lastWrittenWith"] = modVersion
        };
        return new GearwrightWorldState(document, true, "Gearwright world data was created.");
    }

    public static GearwrightWorldState Load(byte[]? bytes, string modVersion)
    {
        if (bytes == null || bytes.Length == 0) return Create(modVersion);

        try
        {
            string json = new UTF8Encoding(false, true).GetString(bytes);
            JObject document = JObject.Parse(json);
            string? format = document.Value<string>("format");
            if (format != null && !string.Equals(format, FormatName, StringComparison.Ordinal))
            {
                return Locked(document, "The stored Gearwright data has an unknown format. It was left untouched.");
            }

            if (!TryReadSchemaVersion(document, out int schemaVersion))
            {
                return Locked(document, "The stored Gearwright schema version is invalid. It was left untouched.");
            }
            if (schemaVersion > CurrentSchemaVersion)
            {
                return Locked(document,
                    $"This world was written with Gearwright schema {schemaVersion}; this build understands {CurrentSchemaVersion}. Writes are disabled to protect the save.");
            }

            bool migrated = false;
            while (schemaVersion < CurrentSchemaVersion)
            {
                schemaVersion = MigrateOneVersion(document, schemaVersion, modVersion);
                migrated = true;
            }

            document["format"] = FormatName;
            string notice = migrated
                ? $"Gearwright world data was upgraded to schema {CurrentSchemaVersion}."
                : $"Gearwright world data schema {CurrentSchemaVersion} loaded.";
            return new GearwrightWorldState(document, true, notice);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return Locked(new JObject(),
                "Gearwright world data could not be read and was left untouched: " + exception.Message);
        }
    }

    public byte[] Serialize(string modVersion)
    {
        if (!CanWrite) throw new InvalidOperationException("Locked Gearwright world data must never be overwritten.");
        document["lastWrittenWith"] = modVersion;
        string json = document.ToString(Formatting.None);
        return new UTF8Encoding(false).GetBytes(json);
    }

    private static GearwrightWorldState Locked(JObject document, string notice)
        => new(document, false, notice);

    private static bool TryReadSchemaVersion(JObject document, out int version)
    {
        JToken? token = document["schemaVersion"];
        if (token == null)
        {
            version = 0;
            return true;
        }
        version = -1;
        return token.Type == JTokenType.Integer
            && int.TryParse(token.ToString(Formatting.None), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out version)
            && version >= 0;
    }

    private static int MigrateOneVersion(JObject document, int fromVersion, string modVersion)
    {
        switch (fromVersion)
        {
            case 0:
                // Schema 0 was the unversioned development shape. Retain every unknown field,
                // add the missing envelope, and move forward without resetting the document.
                document["format"] = FormatName;
                document["schemaVersion"] = 1;
                document["createdWith"] ??= "pre-schema";
                document["migratedWith"] = modVersion;
                return 1;
            default:
                throw new JsonSerializationException($"No migration exists from Gearwright schema {fromVersion}.");
        }
    }
}
