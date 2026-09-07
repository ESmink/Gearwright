using System;
using System.IO;
using System.Linq;
using System.Text;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using Gearwright.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
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
        PipePressureFixture.Run(Check);
        HydraulicRuntimeFixture.Run(Check);
        PressureControlsConsumerWaterUse();
        SprinklerReachUsesStablePressureBands();
        AmbientAudioTracksPressureAndFlow();
        IrrigatorSupportsRespectSpanRules();
        HydraulicSchemasMigrateAdditively();
        HydraulicSchemaOneFixturesStayReadable();
        HydraulicSchemaThreePipeFixtureStaysReadable();
        ReciprocatingPumpStrokePressureAndLoadArePhysical();
        PumpBoundaryPressureDoesNotCancelItsOwnTransfer();
        ReciprocatingPumpPrimesFillsAndDischargesConservatively();
        PumpPipeRunsCarryMoreThanTheVacuumPropagationTrickle();
        PumpDischargeFollowsPistonDisplacement();
        PumpOverloadIsProgressiveAndFinite();
        PassiveHeightCannotCreatePrimingVacuum();
        PumpRuntimeFixture.Run(Check);
        PumpTimingFixture.Run(Check);
        PumpLiquidSurfaceMeetsItsSidesBelowThePiston();
        ReciprocatingLinkageRemainsConnectedInEveryOrientation();
        ReciprocatingCheckMotionFollowsTheStroke();
        InfiniteSourceWaterHasAnEnergyFloor();
        PumpAndCrankSchemasProtectUnknownState();
        DashedDriveShaftCodeKeepsItsFullBaseName();
        RuntimeMechanismAnimationsGenerateEveryEngineFrame();
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

    private static void ReciprocatingPumpStrokePressureAndLoadArePhysical()
    {
        double top = ReciprocatingPumpMath.ChamberVolumeLitres(0);
        double bottom = ReciprocatingPumpMath.ChamberVolumeLitres(Math.PI);
        Check(Math.Abs(top - 4.05) < 0.000001 && Math.Abs(bottom - 0.05) < 0.000001,
            "The piston sweeps four litres and reaches its finite clearance volume");
        Check(ReciprocatingPumpMath.Stroke(bottom, top) == ReciprocatingPumpStroke.Suction &&
              ReciprocatingPumpMath.Stroke(top, bottom) == ReciprocatingPumpStroke.Pressure,
            "Increasing chamber volume selects intake while decreasing volume selects output");

        double vacuum = ReciprocatingPumpMath.ChamberPressureKPa(
            0, 20, PipeContentPhase.Liquid, top);
        double filled = ReciprocatingPumpMath.ChamberPressureKPa(
            4, 20, PipeContentPhase.Liquid, top);
        double compressed = ReciprocatingPumpMath.ChamberPressureKPa(
            4, 20, PipeContentPhase.Liquid, 2);
        Check(vacuum <= -HydraulicMath.AmbientPressureKPa + 0.001 &&
              filled > -2 && compressed > 1000,
            "An empty upstroke draws a vacuum while trapped liquid strongly resists a downstroke");
        Check(ReciprocatingPumpMath.MechanicalResistance(compressed, Math.PI / 2) >
              ReciprocatingPumpMath.MechanicalResistance(filled, Math.PI / 2),
            "Hydraulic back-pressure becomes mechanical resistance on the crank");
    }

    private static void PumpBoundaryPressureDoesNotCancelItsOwnTransfer()
    {
        const double step = .2;
        const double pipeWater = 2;
        double chamberVolume = ReciprocatingPumpMath.ChamberVolumeLitres(0);
        double chamberPressure = ReciprocatingPumpMath.ChamberPressureKPa(
            0, 20, PipeContentPhase.Liquid, chamberVolume);
        LiquidPipePressureSnapshot inlet = new(pipeWater / HydraulicMath.PipeCapacityLitres, 0, 0);
        double networkPressure = new LiquidPipePressureSnapshot(0, 0, 0).WithBoundary(chamberPressure).PressureKPa;
        Check(ReciprocatingPumpMath.CanDrawInfiniteLiquid(networkPressure),
            "An empty pump still projects sufficient suction into its inlet to prime a natural-source nozzle");
        Check(HydraulicMath.RequestedTransferLitres(networkPressure - chamberPressure, step, PipeContentPhase.Liquid) == 0,
            "Regression fixture reproduces the old self-cancelling pump/pipe pressure delta");
        Check(ReciprocatingPumpMath.DrivingBoundaryPressureKPa(-100, ReciprocatingPumpStroke.Pressure, false) == 0 &&
              ReciprocatingPumpMath.DrivingBoundaryPressureKPa(100, ReciprocatingPumpStroke.Suction, true) == 0 &&
              ReciprocatingPumpMath.DrivingBoundaryPressureKPa(-100, ReciprocatingPumpStroke.Suction, false) == 0 &&
              ReciprocatingPumpMath.DrivingBoundaryPressureKPa(100, ReciprocatingPumpStroke.Pressure, true) == 0,
            "Checks never project suction through the outlet or pressure back through the inlet");
        double received = HydraulicMath.BoundedTransferLitres(
            inlet.PressureKPa - chamberPressure, pipeWater, 0, chamberVolume, step, PipeContentPhase.Liquid);
        Check(received == pipeWater && inlet.PressureKPa > networkPressure,
            "Water in the input pipe enters the empty chamber using pressure before this pump's own boundary");
        double lowerPipePressure = HydraulicMath.PropagatedLiquidSuction(networkPressure, 0, -1);
        double reflectedSuction = HydraulicMath.PropagatedLiquidSuction(lowerPipePressure, -1, 0);
        LiquidPipePressureSnapshot connectedInlet = new(.2, 0, reflectedSuction);
        Check(HydraulicMath.BoundedTransferLitres(connectedInlet.PressureKPa - chamberPressure,
                  pipeWater, 0, chamberVolume, step, PipeContentPhase.Liquid) > 0,
            "Suction returning through an adjoining lower pipe does not stop chamber intake");

        LiquidPipePressureSnapshot externalPressure = new(.95, 400, 0);
        LiquidPipePressureSnapshot otherPump = new LiquidPipePressureSnapshot(.95, 0, 0).WithBoundary(400);
        double blockedBySource = HydraulicMath.BoundedTransferLitres(
            200 - externalPressure.PressureKPa, 4, 9.5, 10, step, PipeContentPhase.Liquid);
        double blockedByOtherPump = HydraulicMath.BoundedTransferLitres(
            200 - otherPump.PressureKPa, 4, 9.5, 10, step, PipeContentPhase.Liquid);
        Check(blockedBySource == 0 && blockedByOtherPump == 0,
            "Ignoring a pump's own imposed pressure does not bypass real source or other-pump back-pressure");
        Check(HydraulicMath.BoundedTransferLitres(1000, 4, 10, 10, step, PipeContentPhase.Liquid) == 0 &&
              HydraulicMath.BoundedTransferLitres(1000, 0, 0, 8, step, PipeContentPhase.Liquid) == 0 &&
              HydraulicMath.BoundedTransferLitres(1000, 4, 7.9, 8, step, PipeContentPhase.Liquid) <= .100000001,
            "Pump port transfer cannot overdraw an empty pipe or overfill a liquid pipe/chamber");
        Check(HydraulicMath.BoundedTransferLitres(-1, 4, 0, 10, step, PipeContentPhase.Liquid) == 0 &&
              HydraulicMath.BoundedTransferLitres(100, double.NaN, 0, 10, step, PipeContentPhase.Liquid) == 0,
            "Reverse pressure and malformed donor amounts cannot create a pump transfer");

        double gasPipePressure = HydraulicMath.GasGaugePressure(20, 20);
        double gasChamberPressure = ReciprocatingPumpMath.ChamberPressureKPa(0, 20, PipeContentPhase.Gas, 8);
        double gasReceived = HydraulicMath.BoundedTransferLitres(
            gasPipePressure - gasChamberPressure, 20, 0, double.PositiveInfinity, step, PipeContentPhase.Gas);
        Check(gasReceived > 0 && gasReceived <= 20 &&
              HydraulicMath.BoundedTransferLitres(100, 30, 20, double.PositiveInfinity, step, PipeContentPhase.Gas) > 0,
            "Gas exchange keeps physical gas pressure and is not capped at liquid storage capacity");
    }

    private static void ReciprocatingPumpPrimesFillsAndDischargesConservatively()
    {
        foreach (int direction in new[] { 1, -1 })
        {
            double inletAmount = 0;
            double chamberAmount = 0;
            double outletAmount = 0;
            double totalDrawn = 0;
            double maxChamberAmount = 0;
            double previousVolume = ReciprocatingPumpMath.ChamberVolumeLitres(0);
            bool conserved = true;
            bool respectedCapacity = true;
            bool retainedWhenBlocked = true;
            bool sawBlockedStroke = false;
            for (int tick = 1; tick <= 24 * 8; tick++)
            {
                double volume = ReciprocatingPumpMath.ChamberVolumeLitres(direction * tick * Math.PI / 12);
                ReciprocatingPumpStroke stroke = ReciprocatingPumpMath.Stroke(previousVolume, volume);
                previousVolume = volume;
                double pressure = ReciprocatingPumpMath.ChamberPressureKPa(chamberAmount, 20, PipeContentPhase.Liquid, volume);
                LiquidPipePressureSnapshot inlet = new(inletAmount / 10, 0, 0);
                LiquidPipePressureSnapshot outlet = new(outletAmount / 10, 0, 0);
                double inletNetworkPressure = inlet.WithBoundary(
                    ReciprocatingPumpMath.DrivingBoundaryPressureKPa(pressure, stroke, input: true)).PressureKPa;
                double before = chamberAmount;
                if (stroke == ReciprocatingPumpStroke.Suction)
                {
                    double received = HydraulicMath.BoundedTransferLitres(
                        inlet.PressureKPa - pressure, inletAmount, chamberAmount, volume, .2, PipeContentPhase.Liquid);
                    inletAmount -= received;
                    chamberAmount += received;
                    // Previously trapped liquid may already exceed the current
                    // volume. Only new intake must fit the remaining free space.
                    respectedCapacity &= received <= Math.Max(0, volume - before) + .000001;
                }
                else if (stroke == ReciprocatingPumpStroke.Pressure)
                {
                    double provided = HydraulicMath.BoundedTransferLitres(
                        pressure - outlet.PressureKPa, chamberAmount, outletAmount, 10, .2, PipeContentPhase.Liquid);
                    provided = Math.Min(provided, ReciprocatingPumpMath.DischargeToEquilibriumLitres(
                        chamberAmount, 20, PipeContentPhase.Liquid, volume, outlet.PressureKPa));
                    if (outletAmount >= 10 - .000001 && before > .001)
                    {
                        sawBlockedStroke = true;
                        retainedWhenBlocked &= provided < .000001;
                    }
                    chamberAmount -= provided;
                    outletAmount += provided;
                }
                // The real solver processes the nozzle after pump exchange, so
                // new source water must wait until a subsequent tick for intake.
                double drawn = ReciprocatingPumpMath.InfiniteLiquidIntakeLitres(inletNetworkPressure, 10 - inletAmount, .2);
                inletAmount += drawn;
                totalDrawn += drawn;
                maxChamberAmount = Math.Max(maxChamberAmount, chamberAmount);
                conserved &= Math.Abs(inletAmount + chamberAmount + outletAmount - totalDrawn) < .000001;
                respectedCapacity &= inletAmount >= 0 && inletAmount <= 10.000001 && outletAmount >= 0 && outletAmount <= 10.000001;
            }
            Check(totalDrawn > 0 && maxChamberAmount > 1 && outletAmount > 1,
                $"A dry inlet primes, fills the reservoir, and discharges on later strokes (rotation {direction})");
            Check(conserved && respectedCapacity && sawBlockedStroke && retainedWhenBlocked,
                $"Repeated pump cycles retain trapped liquid (rotation {direction}; conserved={conserved}, bounded intake={respectedCapacity}, saw block={sawBlockedStroke}, retained={retainedWhenBlocked})");
        }
    }

    private static void PumpPipeRunsCarryMoreThanTheVacuumPropagationTrickle()
    {
        foreach (int inputLength in new[] { 1, 4, 7, 8 })
        {
            PumpCircuitResult old = PumpCircuitFixture.Run(inputLength, 3, legacyVacuum: true);
            PumpCircuitResult current = PumpCircuitFixture.Run(inputLength, 3, legacyVacuum: false);
            Console.WriteLine($"[MEASURE] {inputLength}-pipe intake / 3-pipe outlet: old {old.DeliveredPerCycle:F3}, current {current.DeliveredPerCycle:F3} L/cycle; chamber peak {current.MaximumChamberLitres:F3} L");
            Check(current.Bounded && current.MaximumMassError < .000001,
                $"The {inputLength}-pipe pump circuit preserves water and all pipe capacities");
            Check(current.DeliveredPerCycle > 1 && current.MaximumChamberLitres > 2,
                $"The {inputLength}-pipe pump circuit fills its chamber and delivers useful flow");
        }
        foreach (int direction in new[] { 1, -1 })
        {
            PumpCircuitResult dropped = PumpCircuitFixture.Run(7, 3, false, direction, sourceDrop: 1);
            Console.WriteLine($"[MEASURE] 7-pipe intake, 1-block lift, rotation {direction}: {dropped.DeliveredPerCycle:F3} L/cycle; chamber peak {dropped.MaximumChamberLitres:F3} L");
            Check(dropped.Bounded && dropped.MaximumMassError < .000001 &&
                  dropped.DeliveredPerCycle > 1 && dropped.MaximumChamberLitres > 2,
                $"The dropped intake primes and delivers water without loss (rotation {direction})");
        }
    }

    private static void PumpDischargeFollowsPistonDisplacement()
    {
        foreach (double outletPressure in new[] { 0.0, 50, 500 })
        {
            double previousVolume = ReciprocatingPumpMath.ChamberVolumeLitres(0);
            double compression = 1 + outletPressure / ReciprocatingPumpMath.LiquidCompressionStiffnessKPa;
            double amount = previousVolume * compression;
            double initial = amount;
            double totalDelivered = 0;
            int deliverySteps = 0;
            bool gradual = true;
            for (int step = 1; step <= 120; step++)
            {
                double volume = ReciprocatingPumpMath.ChamberVolumeLitres(step * Math.PI / 120);
                double pressure = ReciprocatingPumpMath.ChamberPressureKPa(amount, 20, PipeContentPhase.Liquid, volume);
                double delivery = Math.Min(amount, ReciprocatingPumpMath.RequestedDischargeLitres(
                    pressure - outletPressure, .02, PipeContentPhase.Liquid));
                delivery = Math.Min(delivery, ReciprocatingPumpMath.DischargeToEquilibriumLitres(
                    amount, 20, PipeContentPhase.Liquid, volume, outletPressure));
                amount -= delivery;
                totalDelivered += delivery;
                if (delivery > .000001) deliverySteps++;
                gradual &= delivery >= 0 && delivery <= (previousVolume - volume) * compression + .000001 &&
                    ReciprocatingPumpMath.ChamberPressureKPa(amount, 20, PipeContentPhase.Liquid, volume) >= outletPressure - .000001 &&
                    Math.Abs(initial - amount - totalDelivered) < .000001;
                previousVolume = volume;
            }
            Check(gradual && deliverySteps >= 115 && totalDelivered > 3.9,
                $"Exhaust rises through the downstroke without empty/refill bursts or pressure undershoot ({outletPressure} kPa outlet)");
        }

        Check(ReciprocatingPumpMath.DischargeToEquilibriumLitres(2, 20, PipeContentPhase.Liquid, 5, 0) == 0 &&
              ReciprocatingPumpMath.DischargeToEquilibriumLitres(5, 20, PipeContentPhase.Liquid, 5, 100) == 0 &&
              ReciprocatingPumpMath.DischargeToEquilibriumLitres(double.NaN, 20, PipeContentPhase.Liquid, 5, 0) == 0,
            "Underfilled or insufficiently pressurized chambers do not dump liquid through the output check");
        foreach (double temperature in new[] { 20.0, 120 })
        {
            double discharged = ReciprocatingPumpMath.DischargeToEquilibriumLitres(20, temperature, PipeContentPhase.Gas, 4, 100);
            double remainingPressure = ReciprocatingPumpMath.ChamberPressureKPa(20 - discharged, temperature, PipeContentPhase.Gas, 4);
            Check(discharged > 0 && discharged < 20 && Math.Abs(remainingPressure - 100) < .000001,
                $"Gas discharge also stops at the outlet pressure ({temperature} C)");
        }
        double dry = new LiquidPipePressureSnapshot(0, 0, -100).PressureKPa;
        double partial = new LiquidPipePressureSnapshot(.5, 0, -100).PressureKPa;
        double full = new LiquidPipePressureSnapshot(1, 0, -100).PressureKPa;
        Check(dry == -100 && partial > dry && full > partial && full == HydraulicMath.LiquidBasePressure(1),
            "Dry pipes carry priming vacuum while filling pipes recover a useful water-flow pressure gradient");
    }

    private static void PassiveHeightCannotCreatePrimingVacuum()
    {
        const int height = 32;
        foreach (int wetPipes in new[] { 0, 1, 8, 16, 32 })
        {
            double[] previous = new double[height];
            bool safe = true;
            for (int tick = 0; tick < 1000; tick++)
            {
                double[] next = new double[height];
                for (int y = 0; y < height; y++)
                {
                    double strongest = 0;
                    double weakest = 0;
                    foreach (int neighbor in new[] { y - 1, y + 1 })
                    {
                        if (neighbor < 0 || neighbor >= height) continue;
                        strongest = Math.Max(strongest, HydraulicMath.PropagatedLiquidPressure(
                            previous[neighbor], neighbor, y, neighbor < wetPipes ? 1 : 0));
                        weakest = Math.Min(weakest, HydraulicMath.PropagatedLiquidSuction(previous[neighbor], neighbor, y));
                    }
                    next[y] = new LiquidPipePressureSnapshot(y < wetPipes ? 1 : 0, strongest, weakest).PressureKPa;
                    safe &= next[y] >= -HydraulicMath.EmptyPipeSuctionKPa &&
                        ReciprocatingPumpMath.InfiniteLiquidIntakeLitres(next[y], 10, .2) == 0;
                }
                previous = next;
            }
            Check(safe, $"A 32-block passive riser with {wetPipes} wet pipes never builds extraction vacuum over 1000 steps");
        }
        Check(HydraulicMath.PropagatedLiquidSuction(-HydraulicMath.EmptyPipeSuctionKPa, 0, 100) == 0 &&
              HydraulicMath.PropagatedLiquidSuction(-10, 0, 100) > -10,
            "Neither the empty-pipe bias nor weak suction can be amplified into extraction pressure by height");
        double horizontal = HydraulicMath.PropagatedLiquidSuction(-100, 10, 10);
        double oneBelow = HydraulicMath.PropagatedLiquidSuction(-100, 10, 9);
        Check(Math.Abs(horizontal - (-100 + HydraulicMath.PressurePropagationLossKPa)) < .000001 &&
              Math.Abs(oneBelow - horizontal - HydraulicMath.WaterHeadKPaPerBlock) < .000001 &&
              ReciprocatingPumpMath.CanDrawInfiniteLiquid(oneBelow),
            "Powered suction keeps its horizontal propagation and pays the same one-block source-lift cost");
        Check(HydraulicMath.HydraulicPotential(0, 1, PipeContentPhase.Liquid) >
              HydraulicMath.HydraulicPotential(0, 0, PipeContentPhase.Liquid),
            "Gravity still drives downhill liquid flow after removing vacuum amplification");
    }

    private static void PumpOverloadIsProgressiveAndFinite()
    {
        double service = ReciprocatingPumpMath.ServicePressureKPa;
        foreach (double angle in new[] { .3, Math.PI / 2, Math.PI + .3, -Math.PI / 2 })
        {
            float rated = ReciprocatingPumpMath.MechanicalResistance(service, angle);
            float previous = 0;
            foreach (double multiple in new[] { .25, .5, 1, 1.25, 1.5, 2, 3, 5 })
            {
                float resistance = ReciprocatingPumpMath.MechanicalResistance(service * multiple, angle);
                Check(float.IsFinite(resistance) && resistance > previous,
                    $"Pump load remains finite and keeps rising at {multiple} times service pressure, angle {angle:F2}");
                previous = resistance;
            }
            Check(ReciprocatingPumpMath.MechanicalResistance(service * 2, angle) > rated * 9 &&
                  ReciprocatingPumpMath.MechanicalResistance(service * 3, angle) > rated * 49,
                "Overpressure raises the load steeply instead of plateauing at the old resistance cap");
        }
        double beforeService = ReciprocatingPumpMath.MechanicalResistance(service - .001, Math.PI / 2);
        double afterService = ReciprocatingPumpMath.MechanicalResistance(service + .001, Math.PI / 2);
        Check(afterService > beforeService && afterService - beforeService < .00001,
            "Crossing the service pressure adds no discontinuous lock or load jump");
        double compressed = ReciprocatingPumpMath.ChamberPressureKPa(4, 20, PipeContentPhase.Liquid, 4 / 1.02);
        Check(Math.Abs(compressed - 2 * service) < .00001,
            "Two percent liquid overfill develops twice service pressure without clipping pressure to the rating");
        Check(float.IsFinite(ReciprocatingPumpMath.MechanicalResistance(double.MaxValue, Math.PI / 2)) &&
              double.IsFinite(ReciprocatingPumpMath.ChamberPressureKPa(double.MaxValue, 20, PipeContentPhase.Liquid, .05)),
            "Extreme finite stored amounts cannot inject infinity into the drivetrain");
    }

    private static void PumpLiquidSurfaceMeetsItsSidesBelowThePiston()
    {
        float bottom = ReciprocatingPumpLiquidGeometry.Bottom;
        float upper = ReciprocatingPumpLiquidGeometry.UpperPistonBottom;
        float minX = ReciprocatingPumpLiquidGeometry.MinX;
        float maxX = ReciprocatingPumpLiquidGeometry.MaxX;
        float minZ = ReciprocatingPumpLiquidGeometry.MinZ;
        float maxZ = ReciprocatingPumpLiquidGeometry.MaxZ;
        bool bounded = true;
        for (int degrees = 0; degrees <= 360; degrees += 5)
        foreach (double amount in new[] { 0.0, .05, 2, 4, 8.05, 16 })
        {
            float piston = upper + ReciprocatingPumpMath.VisualPose(degrees * Math.PI / 180, 0).PistonOffsetY;
            float top = ReciprocatingPumpLiquidGeometry.SurfaceHeight(amount, piston,
                ReciprocatingPumpMath.ChamberVolumeLitres(degrees * Math.PI / 180));
            bounded &= top >= bottom && top < piston && top <= upper;
        }
        Check(bounded && ReciprocatingPumpLiquidGeometry.SurfaceHeight(0, upper, 8.05) == bottom &&
              Math.Abs(ReciprocatingPumpLiquidGeometry.SurfaceHeight(8.05, upper, 8.05) -
                  (upper - ReciprocatingPumpLiquidGeometry.PistonInset)) < .000001,
            "Liquid level reaches the upper piston when full and stays below it throughout the stroke");
        bool filledToPiston = true;
        for (int degrees = 0; degrees <= 360; degrees += 5)
        {
            double angle = degrees * Math.PI / 180;
            double volume = ReciprocatingPumpMath.ChamberVolumeLitres(angle);
            float piston = upper + ReciprocatingPumpMath.VisualPose(angle, 0).PistonOffsetY;
            filledToPiston &= Math.Abs(ReciprocatingPumpLiquidGeometry.SurfaceHeight(volume, piston, volume) -
                (piston - ReciprocatingPumpLiquidGeometry.PistonInset)) < .000001;
        }
        Check(filledToPiston, "A liquid-filled chamber meets the piston at every height, including short strokes");

        bool aligned = true;
        foreach (BlockFacing output in BlockFacing.ALLFACES)
        foreach (BlockFacing drive in BlockFacing.ALLFACES.Where(face => face.Axis != output.Axis))
        {
            const float height = .5f;
            Matrixf frame = new();
            frame.Set(PumpOrientation.Matrix(output, drive));
            Matrixf top = new();
            top.Set(frame.Values);
            ReciprocatingPumpLiquidGeometry.ApplyTopPose(top, height);
            // The north quad's first triangle must wind toward the piston.
            float[] a = TransformPoint(top, minX, minZ, 0);
            float[] b = TransformPoint(top, minX, maxZ, 0);
            float[] c = TransformPoint(top, maxX, maxZ, 0);
            float[] d = TransformPoint(top, maxX, minZ, 0);
            aligned &= PointDistance(a, TransformPoint(frame, minX, height, minZ)) < .000001 &&
                PointDistance(b, TransformPoint(frame, minX, height, maxZ)) < .000001 &&
                PointDistance(c, TransformPoint(frame, maxX, height, maxZ)) < .000001 &&
                PointDistance(d, TransformPoint(frame, maxX, height, minZ)) < .000001;
            double nx = (b[1] - a[1]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[1] - a[1]);
            double ny = (b[2] - a[2]) * (c[0] - a[0]) - (b[0] - a[0]) * (c[2] - a[2]);
            double nz = (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
            aligned &= nx * drive.Normali.X + ny * drive.Normali.Y + nz * drive.Normali.Z > 0;
        }
        Check(aligned, "The liquid top joins both window panes and faces the piston in every mounting orientation");
    }

    private static void ReciprocatingLinkageRemainsConnectedInEveryOrientation()
    {
        bool connected = true;
        bool sameDirection = true;
        bool volumeMatchesPiston = true;
        double largestError = 0;
        foreach (BlockFacing axis in new[] { BlockFacing.WEST, BlockFacing.NORTH })
        foreach (BlockFacing shaftSide in new[] { axis, axis.Opposite })
        foreach (BlockFacing output in new[] { axis, axis.Opposite })
        foreach (BlockFacing drive in BlockFacing.ALLFACES.Where(face => face.Axis != axis.Axis))
        // Negative angles also exercise reverse-running networks and wraparound.
        for (int degrees = -360; degrees <= 360; degrees += 5)
        {
            double mechanicalAngle = degrees * Math.PI / 180;
            double pumpAngle = LateralCrankMotion.AngleInFrame(mechanicalAngle, axis, output, drive);
            double shaftAngle = LateralCrankMotion.AngleInFrame(
                mechanicalAngle, axis, shaftSide.Opposite, BlockFacing.UP);
            ReciprocatingPumpVisualPose pose = ReciprocatingPumpMath.VisualPose(
                pumpAngle, ReciprocatingPumpStroke.Stationary);

            Matrixf shaft = new();
            shaft.Set(PumpOrientation.Matrix(shaftSide.Opposite, BlockFacing.UP));
            shaft.Translate(.5f, .5f, .5f).RotateX((float)shaftAngle).Translate(-.5f, -.5f, -.5f);
            float[] pin = TransformPoint(shaft, .5f, .5f + 3f / 16, .5f);

            Matrixf pumpFrame = new();
            // Put the pump one block away from the crank along its local -Y.
            pumpFrame.Translate(-drive.Normali.X, -drive.Normali.Y, -drive.Normali.Z);
            Mat4f.Multiply(pumpFrame.Values, pumpFrame.Values, PumpOrientation.Matrix(output, drive));
            Matrixf rod = new();
            rod.Set(pumpFrame.Values);
            PumpOrientation.ApplyConnectingRodPose(rod, pose);
            float[] rodTop = TransformPoint(rod, .5f, 1.5f + 3f / 16, .5f);
            float[] rodBottom = TransformPoint(rod, .5f, 1.5f - 3f / 16, .5f);
            pumpFrame.Translate(0, pose.PistonOffsetY, 0);
            float[] crosshead = TransformPoint(pumpFrame, .5f, 21f / 16, .5f);
            double error = Math.Max(PointDistance(pin, rodTop), PointDistance(crosshead, rodBottom));
            largestError = Math.Max(largestError, error);
            connected &= error < .000002 && Math.Abs(PointDistance(rodTop, rodBottom) - 6.0 / 16) < .000002;

            // An independent world-axis oracle: UP rotated around vanilla AxisSign.
            float[] expectedPin =
            {
                (float)(.5 - axis.Normali.Z * Math.Sin(mechanicalAngle) * 3 / 16),
                (float)(.5 + Math.Cos(mechanicalAngle) * 3 / 16),
                (float)(.5 + axis.Normali.X * Math.Sin(mechanicalAngle) * 3 / 16)
            };
            sameDirection &= PointDistance(pin, expectedPin) < .000002;
            volumeMatchesPiston &= Math.Abs(ReciprocatingPumpMath.PistonVolumeFraction(pumpAngle) -
                (1 + pose.PistonOffsetY / (6.0 / 16))) < .000002;
        }
        Check(connected, $"Both rod bearings stay pinned for all mount/output/axle sides and reverse rotation (max error {largestError:E2} blocks)");
        Check(sameDirection, "One-sided and through-shaft poses follow the signed vanilla rotation axis without mirroring their orbit");
        Check(volumeMatchesPiston, "Hydraulic chamber volume follows the exact slider-crank piston height");
        Check(Math.Abs(ReciprocatingPumpMath.VisualPose(0, 0).PistonOffsetY) < .000001 &&
              Math.Abs(ReciprocatingPumpMath.VisualPose(Math.PI, 0).PistonOffsetY + 6f / 16) < .000001,
            "The directly rendered piston travels the full six model units between dead centres");
    }

    private static float[] TransformPoint(Matrixf matrix, float x, float y, float z)
    {
        float[] m = matrix.Values;
        return new[]
        {
            m[0] * x + m[4] * y + m[8] * z + m[12],
            m[1] * x + m[5] * y + m[9] * z + m[13],
            m[2] * x + m[6] * y + m[10] * z + m[14]
        };
    }

    private static double PointDistance(float[] a, float[] b) => Math.Sqrt(
        Math.Pow(a[0] - b[0], 2) + Math.Pow(a[1] - b[1], 2) + Math.Pow(a[2] - b[2], 2));

    private static void ReciprocatingCheckMotionFollowsTheStroke()
    {
        ReciprocatingPumpVisualPose suction = ReciprocatingPumpMath.VisualPose(Math.PI / 2, ReciprocatingPumpStroke.Suction);
        ReciprocatingPumpVisualPose pressure = ReciprocatingPumpMath.VisualPose(Math.PI / 2, ReciprocatingPumpStroke.Pressure);
        ReciprocatingPumpVisualPose stopped = ReciprocatingPumpMath.VisualPose(Math.PI / 2, ReciprocatingPumpStroke.Stationary);
        Check(suction.WetIntakeOffsetY > 0 && suction.WetOutputOffsetY == 0 &&
              suction.BreatherExhaustOffsetY > 0 && suction.BreatherIntakeOffsetY == 0 &&
              pressure.WetOutputOffsetY < 0 && pressure.WetIntakeOffsetY == 0 &&
              pressure.BreatherIntakeOffsetY < 0 && pressure.BreatherExhaustOffsetY == 0,
            "Wet and air checks move in opposing stroke pairs, including reverse shaft rotation");
        Check(stopped.WetIntakeOffsetY == 0 && stopped.WetOutputOffsetY == 0 &&
              stopped.BreatherIntakeOffsetY == 0 && stopped.BreatherExhaustOffsetY == 0,
            "A stopped pump closes all visible passive checks");
    }

    private static void InfiniteSourceWaterHasAnEnergyFloor()
    {
        Check(ReciprocatingPumpMath.InfiniteLiquidSourceSuctionKPa >=
              ReciprocatingPumpMath.FutureRapidWaterEnergyBudgetJoulesPerLitre,
            "Infinite source water costs at least the reserved future rapid-water energy per litre");
        Check(!ReciprocatingPumpMath.CanDrawInfiniteLiquid(-69.999) &&
              ReciprocatingPumpMath.CanDrawInfiniteLiquid(-70) &&
              ReciprocatingPumpMath.InfiniteLiquidIntakeLitres(-70, 10, 0.2) > 0,
            "A natural liquid source opens only under the configured strong vacuum");
    }

    private static void PumpAndCrankSchemasProtectUnknownState()
    {
        JObject pumpFixture = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "reciprocating-pump-schema1.json")));
        JObject crankFixture = JObject.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "lateral-crank-schema1.json")));
        Check(pumpFixture.Value<int>("schemaVersion") == 1 &&
              pumpFixture.Value<string>("contentCode") == "game:waterportion" &&
              pumpFixture.SelectToken("futureData.keep")?.Value<bool>() == true,
            "The first reciprocating-pump fixture preserves chamber and unknown state");
        Check(crankFixture.Value<int>("schemaVersion") == 1 &&
              crankFixture.Value<double>("phaseOffsetDegrees") == 135 &&
              crankFixture.SelectToken("futureData.keep")?.Value<bool>() == true,
            "The first lateral-crank fixture preserves its independent journal angle");

        TreeAttribute pump = new();
        pump.SetInt("schemaVersion", 1);
        pump.SetString("futureField", "keep");
        ITreeAttribute readablePump = ReciprocatingPumpStateSchema.PrepareForRead(
            pump, out bool pumpCanWrite, out _);
        Check(pumpCanWrite && readablePump.GetString("futureField") == "keep",
            "Current pump state preserves unrecognized fields");

        TreeAttribute crank = new();
        crank.SetInt("schemaVersion", 2);
        crank.SetDouble("phaseOffsetDegrees", 135);
        ITreeAttribute futureCrank = LateralCrankStateSchema.PrepareForRead(
            crank, out bool crankCanWrite, out string? crankProblem);
        Check(!crankCanWrite && crankProblem == "newer" &&
              futureCrank.GetDouble("phaseOffsetDegrees") == 135,
            "Future crank state remains byte-for-byte eligible for read-only preservation");
    }

    private static void DashedDriveShaftCodeKeepsItsFullBaseName()
    {
        Check(MechanicalCodes.LateralCrankVariantPath("ns") == "lateral-crank-ns" &&
              MechanicalCodes.LateralCrankVariantPath("we") == "lateral-crank-we",
            "Drive-shaft placement retains the dashed public asset code when selecting its rotation");
    }

    private static void RuntimeMechanismAnimationsGenerateEveryEngineFrame()
    {
        foreach (string fileName in new[]
        {
            "lateral-crank-one-sided.json",
            "lateral-crank-through.json",
            "reciprocating-pump-mechanism.json"
        })
        {
            string path = Path.Combine(AppContext.BaseDirectory, "runtime-shapes", fileName);
            try
            {
                Shape? shape = JsonConvert.DeserializeObject<Shape>(File.ReadAllText(path));
                if (shape?.Animations == null)
                {
                    Check(false, $"Runtime animation shape loads: {fileName}");
                    continue;
                }
                shape.InitForAnimations(new SilentLogger(), fileName);
                foreach (Animation animation in shape.Animations)
                {
                    animation.GenerateAllFrames(shape.Elements, shape.JointsById);
                }
                Check(true, $"Vintage Story generates every animation frame: {fileName}");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"Animation frame generation failed for {fileName}: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                Check(false, $"Vintage Story generates every animation frame: {fileName}");
            }
        }
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

    private sealed class SilentLogger : LoggerBase
    {
        protected override void LogImpl(EnumLogType logType, string format, params object[] args) { }
    }
}
