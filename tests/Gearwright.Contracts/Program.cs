using System;
using System.IO;
using System.Text;
using Gearwright.Hydraulics;
using Gearwright.Storage;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;

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
        HydraulicPressureIsDividedEvenly();
        SprinklerPerformanceAndWaterUseAreLinear();
        SprinklerReachUsesStablePressureBands();
        PassivePumpPreservesItsHorizontalReserve();
        HydraulicSchemasMigrateAdditively();
        HydraulicSchemaOneFixturesStayReadable();

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

    private static void HydraulicPressureIsDividedEvenly()
    {
        Check(HydraulicMath.ConsumerPressure(300, 3) == 100,
            "Network pressure is divided evenly between consumers");
        Check(HydraulicMath.ConsumerPressure(300, 0) == 0,
            "A network without consumers exposes no consumer pressure");
    }

    private static void SprinklerPerformanceAndWaterUseAreLinear()
    {
        Check(HydraulicMath.Performance(50) == 0.5,
            "Fifty pressure gives half sprinkler performance");
        Check(HydraulicMath.LitresPerDayPerConsumer(50) == 4,
            "Half performance costs four litres per day");
        Check(HydraulicMath.LitresPerDayPerConsumer(100) == 8,
            "Full performance costs eight litres per day");
    }

    private static void SprinklerReachUsesStablePressureBands()
    {
        Check(HydraulicMath.Reach(24.99) == 1 && HydraulicMath.Reach(25) == 2,
            "The second reach band begins at 25 pressure");
        Check(HydraulicMath.Reach(50) == 3 && HydraulicMath.Reach(75) == 4,
            "The larger reach bands begin at 50 and 75 pressure");
        Check(HydraulicMath.IsInsideCircularReach(3, 2, 4) &&
              !HydraulicMath.IsInsideCircularReach(4, 4, 4),
            "Sprinkler coverage is circular instead of square");
    }

    private static void PassivePumpPreservesItsHorizontalReserve()
    {
        Check(HydraulicMath.PassivePressure(1, true) == 75,
            "A full side tank supplies the gravity drain's pressure limit");
        Check(HydraulicMath.PassivePressure(0.25, true) == 0,
            "The side intake stops at its 25 percent reserve");
        Check(HydraulicMath.PassivePressure(1, false) == 100,
            "A full top tank supplies full sprinkler pressure");
        Check(HydraulicMath.PassivePressure(0.01, false) > 0,
            "The top intake can reach the bottom of its tank");
    }

    private static void HydraulicSchemasMigrateAdditively()
    {
        TreeAttribute schemaOne = new();
        schemaOne.SetInt("schemaVersion", 1);
        schemaOne.SetString("unknownFutureValue", "preserved");

        ITreeAttribute migrated = HydraulicStateSchema.PrepareForRead(
            schemaOne, out bool canWrite, out string? problem);
        Check(canWrite && problem == null, "Hydraulic schema 1 remains writable during migration");
        Check(migrated.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion,
            "Hydraulic schema 1 migrates sequentially through schema 2 to schema 3");
        Check(migrated.GetString("unknownFutureValue") == "preserved",
            "Hydraulic schema migration preserves unknown fields");

        TreeAttribute schemaTwo = new();
        schemaTwo.SetInt("schemaVersion", 2);
        schemaTwo.SetString("facing", "east");
        ITreeAttribute migratedFromTwo = HydraulicStateSchema.PrepareForRead(
            schemaTwo, out bool schemaTwoCanWrite, out string? schemaTwoProblem);
        Check(schemaTwoCanWrite && schemaTwoProblem == null &&
              migratedFromTwo.GetInt("schemaVersion") == 3 &&
              migratedFromTwo.GetString("facing") == "east",
            "Hydraulic schema 2 migrates to schema 3 without losing drain orientation");

        ITreeAttribute reloaded = HydraulicStateSchema.PrepareForRead(
            migrated.Clone(), out bool reloadCanWrite, out string? reloadProblem);
        Check(reloadCanWrite && reloadProblem == null &&
              reloaded.GetString("unknownFutureValue") == "preserved",
            "Migrated hydraulic state saves and reloads without losing unknown fields");
    }

    private static void HydraulicSchemaOneFixturesStayReadable()
    {
        JObject pipe = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "hydraulic-pipe-schema1.json")));
        JObject pump = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "hydraulic-pump-schema1.json")));
        Check(pipe.Value<int>("schemaVersion") == 1 && pipe.Value<int>("addon-0") == 2,
            "The oldest pipe fixture retains the schema-1 sprinkler face");
        Check(pipe.SelectToken("futureData.keep")?.Value<bool>() == true,
            "The pipe fixture carries unknown data that readers must preserve");
        Check(pump.Value<int>("schemaVersion") == 1 &&
              pump.Value<string>("liquidCode") == "game:waterportion",
            "The oldest pump fixture retains its schema and liquid code");
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
