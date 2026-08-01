using System;
using System.Text;
using Gearwright.Storage;
using Newtonsoft.Json.Linq;

namespace Gearwright.Contracts;

internal static class Program
{
    private const string TestVersion = "9.8.7-test";
    private static int failures;

    public static int Main()
    {
        NewDocumentsUseCurrentSchema();
        SchemaZeroMigratesWithoutLosingUnknownData();
        CurrentSchemaRoundTripPreservesUnknownData();
        FutureSchemasAreReadOnly();
        CorruptDataIsReadOnly();
        InvalidSchemaValuesAreReadOnly();
        UnknownFormatsAreReadOnly();

        if (failures == 0)
        {
            Console.WriteLine("All save-compatibility contracts passed.");
            return 0;
        }

        Console.Error.WriteLine($"{failures} save-compatibility contract(s) failed.");
        return 1;
    }

    private static void NewDocumentsUseCurrentSchema()
    {
        GearwrightWorldState state = GearwrightWorldState.Create(TestVersion);
        JObject saved = ReadJson(state.Serialize(TestVersion));
        Check(state.CanWrite, "New state is writable");
        Check(state.SchemaVersion == GearwrightWorldState.CurrentSchemaVersion, "New state uses the current schema");
        Check(saved.Value<string>("createdWith") == TestVersion, "New state records its creating version");
    }

    private static void SchemaZeroMigratesWithoutLosingUnknownData()
    {
        byte[] original = Utf8("{\"legacySetting\":17,\"futureBag\":{\"name\":\"kept\"}}");
        GearwrightWorldState state = GearwrightWorldState.Load(original, TestVersion);
        JObject saved = ReadJson(state.Serialize(TestVersion));
        Check(state.CanWrite, "Schema 0 migration remains writable");
        Check(state.SchemaVersion == 1, "Schema 0 migrates to schema 1");
        Check(saved.Value<int>("legacySetting") == 17, "Schema 0 migration preserves an unknown scalar");
        Check(saved.SelectToken("futureBag.name")?.Value<string>() == "kept", "Schema 0 migration preserves an unknown object");
    }

    private static void CurrentSchemaRoundTripPreservesUnknownData()
    {
        byte[] original = Utf8("{\"format\":\"gearwright-world-state\",\"schemaVersion\":1,\"createdWith\":\"0.1.0\",\"futureList\":[1,2,3]}");
        GearwrightWorldState state = GearwrightWorldState.Load(original, TestVersion);
        JObject saved = ReadJson(state.Serialize(TestVersion));
        Check(saved["futureList"] is JArray array && array.Count == 3, "Current-schema round trip preserves unknown arrays");
        Check(saved.Value<string>("lastWrittenWith") == TestVersion, "Writable state records the last writer");
    }

    private static void FutureSchemasAreReadOnly()
    {
        byte[] original = Utf8("{\"format\":\"gearwright-world-state\",\"schemaVersion\":99,\"valuableFutureData\":true}");
        GearwrightWorldState state = GearwrightWorldState.Load(original, TestVersion);
        Check(!state.CanWrite, "A future schema is write-protected");
        CheckThrows(() => state.Serialize(TestVersion), "A future schema cannot be serialized by this build");
    }

    private static void CorruptDataIsReadOnly()
    {
        GearwrightWorldState state = GearwrightWorldState.Load(Utf8("{not-json"), TestVersion);
        Check(!state.CanWrite, "Malformed JSON is write-protected");
        CheckThrows(() => state.Serialize(TestVersion), "Malformed data cannot be replaced through serialization");
    }

    private static void InvalidSchemaValuesAreReadOnly()
    {
        GearwrightWorldState state = GearwrightWorldState.Load(
            Utf8("{\"format\":\"gearwright-world-state\",\"schemaVersion\":\"one\",\"keepMe\":true}"), TestVersion);
        Check(!state.CanWrite, "A non-integer schema is write-protected");
        Check(state.SchemaVersion == -1, "An invalid schema is reported without throwing");
    }

    private static void UnknownFormatsAreReadOnly()
    {
        GearwrightWorldState state = GearwrightWorldState.Load(
            Utf8("{\"format\":\"some-other-mod\",\"schemaVersion\":1}"), TestVersion);
        Check(!state.CanWrite, "An unknown format is write-protected");
    }

    private static JObject ReadJson(byte[] bytes) => JObject.Parse(Encoding.UTF8.GetString(bytes));
    private static byte[] Utf8(string value) => new UTF8Encoding(false).GetBytes(value);

    private static void Check(bool condition, string name)
    {
        if (condition)
        {
            Console.WriteLine($"[PASS] {name}");
            return;
        }
        Console.Error.WriteLine($"[FAIL] {name}");
        failures += 1;
    }

    private static void CheckThrows(Action action, string name)
    {
        try
        {
            action();
            Check(false, name);
        }
        catch (InvalidOperationException)
        {
            Check(true, name);
        }
    }
}
