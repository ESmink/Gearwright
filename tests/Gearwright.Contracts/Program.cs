using System;
using System.IO;
using System.Linq;
using System.Text;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Gearwright.Storage;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

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
        PressureControlsConsumerWaterUse();
        SprinklerReachUsesStablePressureBands();
        AmbientAudioTracksPressureAndFlow();
        IrrigatorSupportsRespectSpanRules();
        HydraulicSchemasMigrateAdditively();
        HydraulicSchemaOneFixturesStayReadable();
        HydraulicSchemaThreePipeFixtureStaysReadable();
        FlywheelStoresAndReturnsMomentum();
        FlywheelStateIsBoundedAndFinite();
        FlywheelSchemasPreserveUnknownDataAndProtectFutureState();
        FlywheelBoundsFollowTheQueriedMultiblockPart();
        ControlledClutchConservesEqualTerminalMomentum();
        ControlledClutchCouplingIsBoundedAndFinite();
        ControlledClutchAcceleratesLargeCorrections();
        OverrunningCouplingTransfersOnlyFromLeadingInput();
        OverrunningPawlAnimationUsesPhysicalPhase();
        FlywheelNetworkLifecycleAlwaysHasARecoveryAction();

        if (failures == 0)
        {
            Console.WriteLine("All save-compatibility contracts passed.");
            return 0;
        }

        Console.Error.WriteLine($"{failures} save-compatibility contract(s) failed.");
        return 1;
    }

    private static void ControlledClutchConservesEqualTerminalMomentum()
    {
        ClutchCouplingStep step = ClutchCouplingMath.Solve(0.5f, 0.1f, 0.05f);
        Check(step.Changed && step.FirstSpeed < 0.5f && step.SecondSpeed > 0.1f,
            "An engaged controlled clutch exchanges speed toward synchronization");
        Check(Math.Abs((step.FirstSpeed + step.SecondSpeed) - 0.6f) < 0.000001f,
            "The controlled clutch applies equal-and-opposite terminal changes");

        ClutchCouplingStep reverse = ClutchCouplingMath.Solve(-0.4f, 0.2f, 0.05f);
        Check(Math.Abs((reverse.FirstSpeed + reverse.SecondSpeed) + 0.2f) < 0.000001f,
            "Reverse clutch coupling also conserves equal-terminal momentum");
    }

    private static void ControlledClutchCouplingIsBoundedAndFinite()
    {
        ClutchCouplingStep bounded = ClutchCouplingMath.Solve(0, 1, 0.05f);
        Check(Math.Abs(bounded.Transfer) <=
              ClutchCouplingMath.MaximumTransferPerSecond * 0.05f + 0.000001f,
            "Clutch coupling cannot snap across a large speed mismatch in one tick");

        ClutchCouplingStep malformed = ClutchCouplingMath.Solve(float.NaN, 0.2f, 0.05f);
        Check(!malformed.Changed && malformed.Transfer == 0,
            "Malformed clutch inputs are rejected before changing a network");
    }

    private static void ControlledClutchAcceleratesLargeCorrections()
    {
        ClutchCouplingStep small = ClutchCouplingMath.Solve(0, 0.1f, 0.05f);
        ClutchCouplingStep large = ClutchCouplingMath.Solve(0, 1, 0.05f);
        Check(Math.Abs(large.Transfer) > Math.Abs(small.Transfer),
            "Controlled clutch synchronization becomes more aggressive as the speed mismatch grows");
        Check(Math.Abs(large.Transfer) <=
              ClutchCouplingMath.MaximumTransferPerSecond * 0.05f + 0.000001f,
            "Aggressive clutch correction remains bounded per solver step");
    }

    private static void OverrunningCouplingTransfersOnlyFromLeadingInput()
    {
        OverrunningCouplingStep driving = OverrunningCouplingMath.Solve(
            0.6f, 0.1f, 0.05f);
        Check(driving.Engaged && driving.Changed &&
              driving.InputSpeed < 0.6f && driving.OutputSpeed > 0.1f,
            "The overrunning transmission drives from a faster forward input");
        Check(Math.Abs(driving.InputSpeed + driving.OutputSpeed - 0.7f) < 0.000001f,
            "Forward overrunning coupling conserves equal-terminal momentum");

        OverrunningCouplingStep overrun = OverrunningCouplingMath.Solve(
            0.1f, 0.6f, 0.05f);
        Check(!overrun.Engaged && !overrun.Changed && overrun.Transfer == 0,
            "A faster forward output freewheels without back-driving the input");

        OverrunningCouplingStep sticky = OverrunningCouplingMath.Solve(
            0.2f, 0.205f, 0.05f, true, 0);
        Check(sticky.Engaged && sticky.Changed &&
              sticky.InputSpeed > 0.2f && sticky.OutputSpeed < 0.205f,
            "An engaged ratchet absorbs a small output lead instead of chattering open");
        OverrunningCouplingStep released = OverrunningCouplingMath.Solve(
            0.2f, 0.22f, 0.05f, true, 0);
        Check(!released.Engaged && !released.Changed,
            "The ratchet releases after the output becomes measurably faster");

        OverrunningCouplingStep ordinaryContact = OverrunningCouplingMath.Solve(
            0.3f, 0.1f, 0.05f, true, 0);
        OverrunningCouplingStep threatenedContact = OverrunningCouplingMath.Solve(
            0.3f,
            0.1f,
            0.05f,
            true,
            -OverrunningPawlMath.FullContactThreatPhaseLag);
        Check(Math.Abs(threatenedContact.Transfer) > Math.Abs(ordinaryContact.Transfer),
            "A loaded pawl receives a stronger bounded exchange before crossing its locking face");
        Check(Math.Abs(
                  threatenedContact.InputSpeed + threatenedContact.OutputSpeed - 0.4f) <
              0.000001f,
            "Phase-aware contact correction remains equal and opposite");

        OverrunningCouplingUpdate acquiredLock = OverrunningCouplingMath.Advance(
            default, 0.3f, 0.1f, 0, 0.05f);
        OverrunningCouplingUpdate loadedLock = OverrunningCouplingMath.Advance(
            acquiredLock.State,
            0.3f,
            0.1f,
            -OverrunningPawlMath.FullContactThreatPhaseLag,
            0.05f);
        Check(acquiredLock.State.Engaged && loadedLock.State.PhaseError < 0 &&
              loadedLock.ContactThreat > 0.99f,
            "The live lock tracker accumulates input-side tooth lag across solver ticks");

        OverrunningCouplingStep reverseDrive = OverrunningCouplingMath.Solve(
            -0.6f, -0.1f, 0.05f);
        Check(reverseDrive.Engaged && reverseDrive.Changed &&
              reverseDrive.InputSpeed > -0.6f && reverseDrive.OutputSpeed < -0.1f,
            "The ratchet mirrors itself and drives from a faster reverse input");
        Check(Math.Abs(reverseDrive.InputSpeed + reverseDrive.OutputSpeed + 0.7f) < 0.000001f,
            "Reverse overrunning coupling conserves equal-terminal momentum");

        OverrunningCouplingStep reverseOverrun = OverrunningCouplingMath.Solve(
            -0.1f, -0.6f, 0.05f);
        Check(!reverseOverrun.Engaged && !reverseOverrun.Changed,
            "A faster reverse output freewheels through the mirrored ratchet");
        Check(!OverrunningCouplingMath.Solve(0, 0.6f, 0.05f).Engaged &&
              !OverrunningCouplingMath.Solve(0, -0.6f, 0.05f).Engaged,
            "A stopped input cannot be back-driven from either output direction");
        Check(OverrunningCouplingMath.OperatingDirection(-0.2f, 0) == -1 &&
              OverrunningCouplingMath.OperatingDirection(0, -0.2f) == -1 &&
              OverrunningCouplingMath.OperatingDirection(0, 0.2f) == 1,
            "Handedness follows the moving input, then the freewheeling output");

        OverrunningCouplingStep malformed = OverrunningCouplingMath.Solve(
            float.NaN, 0, 0.05f);
        Check(!malformed.Engaged && !malformed.Changed,
            "Malformed overrunning speeds cannot change either network");
    }

    private static void OverrunningPawlAnimationUsesPhysicalPhase()
    {
        float pitch = OverrunningPawlMath.ToothPitch;
        float rest = OverrunningPawlMath.Lift(.2f * pitch);
        float climbing = OverrunningPawlMath.Lift(.55f * pitch);
        float peak = OverrunningPawlMath.Lift(.8f * pitch);
        float dropping = OverrunningPawlMath.Lift(.9925f * pitch);
        Check(rest == 0 && climbing > 0 && climbing < peak &&
              Math.Abs(peak - OverrunningPawlMath.MaximumLift) < 0.000001f &&
              dropping > 0 && dropping < peak &&
              OverrunningPawlMath.Lift(0) == 0,
            "The pawl starts seated, climbs slowly, peaks late, and drops quickly at tooth clearance");

        float sample = .57f * pitch;
        Check(Math.Abs(OverrunningPawlMath.Lift(sample) -
                       OverrunningPawlMath.Lift(sample + 2 * MathF.PI / 3)) < 0.000001f,
            "All three pawls share one phase on a fifteen-tooth wheel");
        Check(OverrunningPawlMath.Lift(float.NaN) == 0,
            "Malformed animation phase returns a safe seated pawl");
    }

    private static void FlywheelNetworkLifecycleAlwaysHasARecoveryAction()
    {
        Check(FlywheelNetworkPlan.Decide(false, 0, false) ==
              FlywheelNetworkAction.CreateStandalone,
            "An isolated flywheel creates a one-node mechanical network");
        Check(FlywheelNetworkPlan.Decide(false, 1, true) ==
              FlywheelNetworkAction.DiscoverFromNeighbour,
            "A networkless flywheel rediscovers from a connected drivetrain");
        Check(FlywheelNetworkPlan.Decide(true, 1, true) ==
              FlywheelNetworkAction.ConnectNeighbour,
            "A flywheel joins a new or rebuilt neighbouring network");
        Check(FlywheelNetworkPlan.Decide(true, 2, false) ==
              FlywheelNetworkAction.None,
            "A healthy through-network is left unchanged");
    }

    private static void FlywheelBoundsFollowTheQueriedMultiblockPart()
    {
        static void CheckLocal(Cuboidf[] boxes, string label)
        {
            Check(boxes.Length > 0, label + " has collision geometry");
            Check(boxes.All(box =>
                    box.X1 >= 0 && box.Y1 >= 0 && box.Z1 >= 0 &&
                    box.X2 <= 1 && box.Y2 <= 1 && box.Z2 <= 1),
                label + " clips every box to the queried block");
        }

        CheckLocal(FlywheelBounds.ForPart(false, new Vec3i(1, 0, 0)), "north/south left part");
        CheckLocal(FlywheelBounds.ForPart(false, new Vec3i(-1, 0, 0)), "north/south right part");
        CheckLocal(FlywheelBounds.ForPart(false, new Vec3i(0, -1, 0)), "north/south top part");
        CheckLocal(FlywheelBounds.ForPart(true, new Vec3i(0, 0, 1)), "west/east near part");
        CheckLocal(FlywheelBounds.ForPart(true, new Vec3i(0, 0, -1)), "west/east far part");
        Check(FlywheelBounds.ForPart(false, new Vec3i(3, 0, 0)).Length == 0,
            "parts outside the 3x3 footprint have no flywheel bounds");
    }

    private static void FlywheelStoresAndReturnsMomentum()
    {
        FlywheelExchange charging = FlywheelMath.Exchange(0.1f, 0.5f);
        Check(charging.StoredSpeed > 0.1f && charging.Torque == 0 &&
              charging.Resistance > FlywheelMath.BaseBearingResistance,
            "A faster vanilla network charges the flywheel through added resistance");

        FlywheelExchange discharging = FlywheelMath.Exchange(0.5f, 0.1f);
        Check(discharging.StoredSpeed < 0.5f && discharging.Torque > 0 &&
              Math.Abs(discharging.Resistance - FlywheelMath.BaseBearingResistance) < 0.000001,
            "A faster flywheel returns torque while its stored speed falls");

        FlywheelExchange reverse = FlywheelMath.Exchange(0.4f, -0.1f);
        Check(reverse.Torque > 0 && reverse.StoredSpeed < 0.4f,
            "Stored momentum resists a sudden network reversal");
    }

    private static void FlywheelStateIsBoundedAndFinite()
    {
        Check(FlywheelMath.SanitizeSpeed(float.NaN) == 0 &&
              FlywheelMath.SanitizeSpeed(float.PositiveInfinity) == 0,
            "Malformed flywheel speeds never enter the mechanical solver");
        Check(FlywheelMath.SanitizeSpeed(9) == FlywheelMath.MaxSupportedSpeed &&
              FlywheelMath.SanitizeSpeed(-9) == -FlywheelMath.MaxSupportedSpeed,
            "Persisted flywheel speed is clamped to the supported vanilla range");
        Check(FlywheelMath.RevolutionsPerMinute(0.5f) > 0,
            "Flywheel inspection converts network speed to a readable RPM value");
    }

    private static void FlywheelSchemasPreserveUnknownDataAndProtectFutureState()
    {
        TreeAttribute current = new();
        current.SetInt("schemaVersion", FlywheelStateSchema.CurrentVersion);
        current.SetDouble("storedSpeed", 0.25);
        current.SetString("futureNote", "keep");
        ITreeAttribute readable = FlywheelStateSchema.PrepareForRead(
            current, out bool canWrite, out string? problem);
        Check(canWrite && problem == null && readable.GetString("futureNote") == "keep",
            "Current flywheel state is writable and preserves unknown fields");

        TreeAttribute future = new();
        future.SetInt("schemaVersion", 99);
        future.SetBytes("futurePayload", new byte[] { 1, 2, 3 });
        ITreeAttribute protectedState = FlywheelStateSchema.PrepareForRead(
            future, out bool futureCanWrite, out string? futureProblem);
        Check(!futureCanWrite && futureProblem == "newer" &&
              protectedState.GetBytes("futurePayload")?.SequenceEqual(new byte[] { 1, 2, 3 }) == true,
            "Future flywheel state is read-only and retained byte for byte");

        TreeAttribute malformed = new();
        malformed.SetString("schemaVersion", "one");
        FlywheelStateSchema.PrepareForRead(
            malformed, out bool malformedCanWrite, out string? malformedProblem);
        Check(!malformedCanWrite && malformedProblem == "invalid",
            "A malformed flywheel schema is read-only");
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

    private static void AmbientAudioTracksPressureAndFlow()
    {
        Check(HydraulicMath.PressureWarningIntensity(100) == 0 &&
              Math.Abs(HydraulicMath.PressureWarningIntensity(125) - 0.5) < 0.000001 &&
              HydraulicMath.PressureWarningIntensity(150) == 1 &&
              HydraulicMath.PressureWarningIntensity(1000) == 1,
            "Pressure-warning intensity rises only through the 100-150 kPa warning band");
        Check(HydraulicMath.AudioFlowIntensity(0) == 0 &&
              Math.Abs(HydraulicMath.AudioFlowIntensity(2) - 0.5) < 0.000001 &&
              HydraulicMath.AudioFlowIntensity(-8) == 1,
            "Ambient water intensity follows flow magnitude and reaches its extreme at 8 L/s");
        Check(HydraulicMath.AmbientAudioRange(0.75) ==
                  HydraulicMath.LocalAmbientAudioRangeBlocks &&
              HydraulicMath.AmbientAudioRange(1) ==
                  HydraulicMath.ExtremeAmbientAudioRangeBlocks &&
              HydraulicMath.LocalAmbientAudioRangeBlocks == 2.8 &&
              HydraulicMath.ExtremeAmbientAudioRangeBlocks == 16,
            "Ambient sounds use doubled local and extreme ranges without changing their intensity band");
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

    private static void PressureControlsConsumerWaterUse()
    {
        Check(HydraulicMath.Performance(50) == 0.5,
            "Fifty pressure gives half sprinkler performance");
        Check(Math.Abs(HydraulicMath.LitresPerDayPerConsumer(50) -
                       480 * Math.Sqrt(.5)) < .001,
            "Sprinkler flow follows the square-root pressure curve");
        Check(HydraulicMath.LitresPerDayPerConsumer(100) == 480,
            "Full sprinkler pressure costs 480 litres per day");
        Check(HydraulicMath.IrrigatorPerformance(5) == .25 &&
              HydraulicMath.IrrigatorLitresPerDay(5) == 36 &&
              HydraulicMath.IrrigatorLitresPerDay(20) == 72,
            "The irrigator reaches its cheaper full flow at twenty kPa");
        Check(HydraulicMath.IrrigatorMaximumMoisture == .8,
            "The irrigator cannot hydrate farmland beyond eighty percent");
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

    private static void IrrigatorSupportsRespectSpanRules()
    {
        Check(IrrigatorSupportPlanner.TryPlan(
                1, new[] { true }, out int[] standalone, out _) &&
              standalone[0] == (IrrigatorSupportPlanner.NegativeSupport |
                                IrrigatorSupportPlanner.PositiveSupport),
            "A standalone irrigator shows both supports");

        Check(IrrigatorSupportPlanner.TryPlan(
                9, new[] { true, true, true, true, true, true, true, true, true },
                out int[] run, out _) &&
              run.Count(mask => mask != 0) == 3 &&
              run[0] != 0 && run[4] != 0 && run[8] != 0 &&
              run.All(mask => mask is 0 or 1 or 2),
            "Long runs prefer endpoints and never show two supports on one pipe");

        Check(!IrrigatorSupportPlanner.TryPlan(
                6, new[] { true, false, false, false, false, true },
                out _, out int breakIndex) && breakIndex == 4,
            "A span with four unsupported pipes identifies the pipe that must break off");
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
            "Hydraulic schema 1 migrates sequentially through irrigator schema 7");
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
            "Hydraulic schema 2 reaches schema 7 without losing retained attachment state");

        TreeAttribute schemaThree = new();
        schemaThree.SetInt("schemaVersion", 3);
        schemaThree.SetString("networkFlowDirection", "south");
        ITreeAttribute migratedFromThree = HydraulicStateSchema.PrepareForRead(
            schemaThree, out bool schemaThreeCanWrite, out string? schemaThreeProblem);
        Check(schemaThreeCanWrite && schemaThreeProblem == null &&
              migratedFromThree.GetInt("schemaVersion") == HydraulicStateSchema.CurrentVersion &&
              !migratedFromThree.HasAttribute("networkFlowDirection"),
            "Hydraulic schema 3 discards the obsolete bufferless flow cache before schema 7");

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
              migratedFromFive.GetInt("schemaVersion") == 7 &&
              migratedFromFive.GetInt("port-0") == 1 &&
              migratedFromFive.GetInt("port-4") == 0,
            "Schema 5 free faces become explicit ports while attachment faces remain closed");

        TreeAttribute schemaSix = new();
        schemaSix.SetInt("schemaVersion", 6);
        schemaSix.SetString("unknownPipeValue", "preserved");
        ITreeAttribute migratedFromSix = HydraulicStateSchema.PrepareForRead(
            schemaSix, out bool schemaSixCanWrite, out string? schemaSixProblem);
        Check(schemaSixCanWrite && schemaSixProblem == null &&
              migratedFromSix.GetInt("schemaVersion") == 7 &&
              migratedFromSix.GetString("unknownPipeValue") == "preserved",
            "Schema 6 migrates additively for flanges and Irrigator Pipe state");

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
