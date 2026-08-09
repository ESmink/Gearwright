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
        PipeVolumeAndGasPressureArePhysical();
        AirNozzlesRespectPhaseAndOpeningHeight();
        LiquidPressureUsesPreviousNeighbourState();
        HydraulicPotentialSupportsWaterTowers();
        TextureScrollTracksPressureDrivenFlow();
        PipeFlowPlanningIsConservative();
        SprinklerPerformanceAndWaterUseAreLinear();
        SprinklerReachUsesStablePressureBands();
        HydraulicSchemasMigrateAdditively();
        HydraulicSchemaOneFixturesStayReadable();
        HydraulicSchemaThreePipeFixtureStaysReadable();

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

    private static void PipeVolumeAndGasPressureArePhysical()
    {
        Check(HydraulicMath.PipeCapacityLitres == 10,
            "Every pipe has a ten litre physical volume");
        Check(Math.Abs(HydraulicMath.GasGaugePressure(10, 20)) < 0.001,
            "Ten standard litres of room-temperature gas occupy a pipe at ambient pressure");
        Check(HydraulicMath.GasGaugePressure(20, 20) > 100 &&
              HydraulicMath.GasGaugePressure(10, 100) > 0,
            "Gas pressure rises with compression and temperature");
        double amount = HydraulicMath.GasStandardLitresForGaugePressure(250, 100);
        Check(Math.Abs(HydraulicMath.GasGaugePressure(amount, 100) - 250) < 0.001,
            "Configured gas pressure and standard-litre storage are invertible");
    }

    private static void AirNozzlesRespectPhaseAndOpeningHeight()
    {
        Check(HydraulicMath.LiquidOverflowLitres(9.5) == 0 &&
              HydraulicMath.LiquidOverflowLitres(10) == 0 &&
              HydraulicMath.LiquidOverflowLitres(12.25) == 2.25,
            "An upward liquid nozzle releases only content beyond pipe capacity");
        double ambientAmount = HydraulicMath.GasStandardLitresForGaugePressure(0, 100);
        Check(HydraulicMath.GasVentableStandardLitres(ambientAmount, 100) < 0.000001 &&
              Math.Abs(HydraulicMath.GasVentableStandardLitres(ambientAmount + 3, 100) - 3) < 0.000001,
            "An air nozzle retains gas at ambient pressure and vents only overpressure");
    }

    private static void LiquidPressureUsesPreviousNeighbourState()
    {
        double transmitted = HydraulicMath.PropagatedLiquidPressure(0, 11, 10);
        Check(Math.Abs(transmitted - HydraulicMath.WaterHeadKPaPerBlock) < 0.001,
            "A liquid pipe receives one block of hydrostatic head from the pipe above");
        Check(Math.Abs(HydraulicMath.PropagatedLiquidPressure(0, 11, 10, 0)) < 0.001,
            "An empty upper pipe cannot generate a false water column");
        Check(HydraulicMath.LiquidPressureFromPrevious(1, transmitted) == transmitted,
            "A full pipe transmits the strongest pressure gathered from the previous step");
        Check(HydraulicMath.LiquidBasePressure(0) < 0 &&
              HydraulicMath.LiquidBasePressure(0.1) > HydraulicMath.LiquidBasePressure(0) &&
              HydraulicMath.LiquidPressureFromPrevious(0, 0) < 0,
            "Empty and nearly empty pipes expose a bounded suction pressure");
        Check(HydraulicMath.RequestedTransferLitres(10, 0.2, PipeContentPhase.Liquid) > 0,
            "A pressure difference requests a bounded liquid transfer for the next commit");
    }

    private static void HydraulicPotentialSupportsWaterTowers()
    {
        double upper = HydraulicMath.HydraulicPotential(0, 3, PipeContentPhase.Liquid);
        double lower = HydraulicMath.HydraulicPotential(
            3 * HydraulicMath.WaterHeadKPaPerBlock, 0, PipeContentPhase.Liquid);
        Check(Math.Abs(upper - lower) < 0.001,
            "Three blocks of water head balance a pipe three blocks higher");
    }

    private static void TextureScrollTracksPressureDrivenFlow()
    {
        double slow = HydraulicMath.TextureScrollCyclesPerSecond(
            HydraulicMath.PipeConductanceLitresPerSecondPerKPa, PipeContentPhase.Liquid);
        double fast = HydraulicMath.TextureScrollCyclesPerSecond(
            HydraulicMath.PipeConductanceLitresPerSecondPerKPa * 25, PipeContentPhase.Liquid);
        Check(HydraulicMath.TextureScrollCyclesPerSecond(0, PipeContentPhase.Liquid) == 0 &&
              fast > slow && slow > 0,
            "Pipe texture speed rises with the effective pressure deficit");

        double liquidAtTenKPa = HydraulicMath.TextureScrollCyclesPerSecond(
            HydraulicMath.PipeConductanceLitresPerSecondPerKPa * 10, PipeContentPhase.Liquid);
        double gasAtTenKPa = HydraulicMath.TextureScrollCyclesPerSecond(
            HydraulicMath.GasConductanceStandardLitresPerSecondPerKPa * 10, PipeContentPhase.Gas);
        Check(Math.Abs(liquidAtTenKPa - gasAtTenKPa) < 0.000001,
            "Equal pressure deficits produce equal liquid and gas texture speed");
    }

    private static void PipeFlowPlanningIsConservative()
    {
        double[] amounts = { 10, 0, 9.5 };
        double[] capacities = { 10, 10, 10 };
        PipeTransferIntent[] intents =
        {
            new(0, 1, 8),
            new(0, 2, 8)
        };
        double[] moved = PipeFlowSolver.ScaleTransfers(amounts, capacities, intents, 0.45);
        double finalDonor = amounts[0] - moved[0] - moved[1];
        double finalOne = amounts[1] + moved[0];
        double finalTwo = amounts[2] + moved[1];
        Check(moved[0] + moved[1] <= 4.5 && finalDonor >= 0,
            "Simultaneous edges cannot overdraw a donor");
        Check(finalOne <= capacities[1] && finalTwo <= capacities[2],
            "Simultaneous edges cannot overfill a liquid receiver");
        Check(Math.Abs(finalDonor + finalOne + finalTwo - amounts[0] - amounts[1] - amounts[2]) < 0.000001,
            "Scaled internal transfers conserve pipe content exactly");

        PipeTransferIntent[] reversed = { intents[1], intents[0] };
        double[] reversedMoved = PipeFlowSolver.ScaleTransfers(amounts, capacities, reversed, 0.45);
        Check(Math.Abs(moved[0] - reversedMoved[1]) < 0.000001 &&
              Math.Abs(moved[1] - reversedMoved[0]) < 0.000001,
            "Transfer scaling is independent of connection enumeration order");

        PipeTransferIntent[] invalidRequests = { new(0, 1, double.NaN), new(0, 1, double.PositiveInfinity) };
        double[] ignored = PipeFlowSolver.ScaleTransfers(amounts, capacities, invalidRequests, 0.45);
        Check(ignored[0] == 0 && ignored[1] == 0,
            "Non-finite transfer requests cannot poison a simulation step");
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

    private static void HydraulicSchemasMigrateAdditively()
    {
        TreeAttribute schemaOne = new();
        schemaOne.SetInt("schemaVersion", 1);
        schemaOne.SetString("unknownFutureValue", "preserved");

        ITreeAttribute migrated = HydraulicStateSchema.PrepareForRead(
            schemaOne, out bool canWrite, out string? problem);
        Check(canWrite && problem == null, "Hydraulic schema 1 remains writable during migration");
        Check(migrated.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion,
            "Hydraulic schema 1 migrates sequentially through explicit-port schema 6");
        Check(migrated.GetString("unknownFutureValue") == "preserved",
            "Hydraulic schema migration preserves unknown fields");

        TreeAttribute schemaTwo = new();
        schemaTwo.SetInt("schemaVersion", 2);
        schemaTwo.SetString("facing", "east");
        ITreeAttribute migratedFromTwo = HydraulicStateSchema.PrepareForRead(
            schemaTwo, out bool schemaTwoCanWrite, out string? schemaTwoProblem);
        Check(schemaTwoCanWrite && schemaTwoProblem == null &&
              migratedFromTwo.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion &&
              migratedFromTwo.GetString("facing") == "east",
            "Hydraulic schema 2 reaches schema 6 without losing retained attachment state");

        TreeAttribute schemaThree = new();
        schemaThree.SetInt("schemaVersion", 3);
        schemaThree.SetString("networkFlowDirection", "south");
        ITreeAttribute migratedFromThree = HydraulicStateSchema.PrepareForRead(
            schemaThree, out bool schemaThreeCanWrite, out string? schemaThreeProblem);
        Check(schemaThreeCanWrite && schemaThreeProblem == null &&
              migratedFromThree.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion &&
              !migratedFromThree.HasAttribute("networkFlowDirection"),
            "Hydraulic schema 3 discards the obsolete bufferless flow cache before schema 6");

        TreeAttribute schemaFour = new();
        schemaFour.SetInt("schemaVersion", 4);
        schemaFour.SetInt("addon-2", 3);
        schemaFour.SetDouble("networkPressure", 500);
        ITreeAttribute migratedFromFour = HydraulicStateSchema.PrepareForRead(
            schemaFour, out bool schemaFourCanWrite, out string? schemaFourProblem);
        Check(schemaFourCanWrite && schemaFourProblem == null &&
              migratedFromFour.GetInt("addon-2") == 3 &&
              !migratedFromFour.HasAttribute("networkPressure") &&
              migratedFromFour.GetInt("port-0") == 1 &&
              migratedFromFour.GetInt("port-2") == 0,
            "The authorized break keeps attachments and removes obsolete aggregate pressure");

        TreeAttribute schemaFive = new();
        schemaFive.SetInt("schemaVersion", 5);
        schemaFive.SetInt("addon-4", 1);
        ITreeAttribute migratedFromFive = HydraulicStateSchema.PrepareForRead(
            schemaFive, out bool schemaFiveCanWrite, out string? schemaFiveProblem);
        Check(schemaFiveCanWrite && schemaFiveProblem == null &&
              migratedFromFive.GetInt("schemaVersion") == 6 &&
              migratedFromFive.GetInt("port-0") == 1 &&
              migratedFromFive.GetInt("port-4") == 0,
            "Schema 5 free faces become explicit ports while attachment faces remain closed");

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

    private static void HydraulicSchemaThreePipeFixtureStaysReadable()
    {
        JObject pipe = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "hydraulic-pipe-schema3.json")));
        Check(pipe.Value<int>("schemaVersion") == 3 && pipe.Value<int>("addon-1") == 1,
            "The last pre-intake pipe fixture retains its schema-3 glass window");
        Check(pipe.SelectToken("futureData.keep")?.Value<bool>() == true,
            "The schema-3 pipe fixture carries unknown data that migration must preserve");
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
