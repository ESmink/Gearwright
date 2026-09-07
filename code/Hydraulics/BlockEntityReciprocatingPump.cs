using System;
using System.Text;
using Gearwright.Mechanics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public sealed class BlockEntityReciprocatingPump : BlockEntity, IHydraulicNetworkNode
{
    private const string StorageKey = "gearwrightReciprocatingPump";
    private const double EmptyEpsilonLitres = .000001;
    private ITreeAttribute preservedState = new TreeAttribute();
    private bool canWrite = true;
    private bool registered;
    private bool volumeInitialized;
    private double previousVolumeLitres = ReciprocatingPumpMath.ChamberVolumeLitres(0);
    private AssetLocation? contentCode;
    private double amountLitres;
    private double temperatureC = 20;
    private double chamberPressureKPa = -HydraulicMath.AmbientPressureKPa;
    private double chamberVolumeLitres = ReciprocatingPumpMath.ChamberVolumeLitres(0);
    private double throughputLitresPerSecond;
    private string statusCode = "idle";
    private double transferStepSeconds = .2;
    private bool intakeOpen;
    private bool outputOpen;
    private double frameIntakeLitres;
    private double frameOutputLitres;
    private double frameSeconds;
    private BlockEntityFluidPipe? inputPipe;
    private BlockEntityFluidPipe? outputPipe;
    private double inputPressure;
    private double outputPressure;
    private ReciprocatingPumpRenderer? renderer;

    public BlockPos Position => Pos;
    public BlockFacing DriveFace { get; private set; } = BlockFacing.UP;
    public BlockFacing OutputFace { get; private set; } = BlockFacing.EAST;
    public BlockFacing InputFace => OutputFace.Opposite;
    public AssetLocation? CurrentContentCode => contentCode;
    public double ContentAmountLitres => amountLitres;
    public double ContentTemperatureC => temperatureC;
    public double ChamberPressureKPa => chamberPressureKPa;
    public double ChamberVolumeLitres => chamberVolumeLitres;
    public double ThroughputLitresPerSecond => throughputLitresPerSecond;
    public string StatusCode => statusCode;
    internal bool UsesNetworkSolver { get; set; }
    internal bool CanWriteState => canWrite;
    public ReciprocatingPumpStroke CurrentStroke { get; private set; }
    public bool HasContent => contentCode != null && amountLitres > 0;
    public PipeContentPhase ContentPhase => contentCode != null && PipeContent.IsSteam(contentCode)
        ? PipeContentPhase.Gas
        : PipeContentPhase.Liquid;
    public double FillFraction => ContentPhase == PipeContentPhase.Gas
        ? 1 - Math.Exp(-Math.Max(0, amountLitres) / ReciprocatingPumpMath.StrokeCapacityLitres)
        : Math.Clamp(amountLitres / ReciprocatingPumpMath.StrokeCapacityLitres, 0, 1);

    public bool CanConnect(BlockFacing face) => face == InputFace || face == OutputFace;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            api.ModLoader.GetModSystem<HydraulicNetworkSystem>().RegisterPump(this);
            registered = true;
        }
        else if (api is ICoreClientAPI capi)
        {
            renderer = new ReciprocatingPumpRenderer(this, capi);
            capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "gearwright-reciprocating-pump-pose");
        }
    }

    public void ConfigurePlacement(BlockFacing driveFace, BlockFacing outputFace)
    {
        if (!canWrite || driveFace.Axis == outputFace.Axis) return;
        DriveFace = driveFace;
        OutputFace = outputFace;
        foreach (BlockFacing face in new[] { InputFace, OutputFace })
        {
            if (Api?.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face)) is BlockEntityFluidPipe pipe)
            {
                pipe.EnableReciprocalPort(face.Opposite);
            }
            Api?.World.BlockAccessor.MarkBlockDirty(Pos.AddCopy(face));
        }
        MarkDirty(true);
        Api?.World.BlockAccessor.TriggerNeighbourBlockUpdate(Pos);
    }

    internal void PrepareSimulation(double stepSeconds = .2)
    {
        transferStepSeconds = double.IsFinite(stepSeconds) && stepSeconds > 0 ? stepSeconds : .2;
        throughputLitresPerSecond = 0;
        intakeOpen = false;
        outputOpen = false;
        if (!canWrite)
        {
            CurrentStroke = ReciprocatingPumpStroke.Stationary;
            statusCode = "state-read-only";
            return;
        }
        BEBehaviorMPLateralCrank? crank = FindCrank();
        if (crank == null)
        {
            CurrentStroke = ReciprocatingPumpStroke.Stationary;
            statusCode = "disconnected";
            chamberPressureKPa = PressureAt(chamberVolumeLitres);
            return;
        }

        double angle = crank.PumpAngle(this);
        double nextVolume = ReciprocatingPumpMath.ChamberVolumeLitres(angle);
        CurrentStroke = volumeInitialized
            ? ReciprocatingPumpMath.Stroke(previousVolumeLitres, nextVolume)
            : ReciprocatingPumpStroke.Stationary;
        volumeInitialized = true;
        previousVolumeLitres = nextVolume;
        chamberVolumeLitres = nextVolume;
        chamberPressureKPa = PressureAt(nextVolume);
        // A stopped, pressurized chamber must still be able to drain after the
        // outlet is unblocked; requiring fresh movement would deadlock it.
        if (CurrentStroke == ReciprocatingPumpStroke.Stationary && chamberPressureKPa > HydraulicMath.FlowDeadbandKPa)
            CurrentStroke = ReciprocatingPumpStroke.Pressure;
        statusCode = CurrentStroke switch
        {
            ReciprocatingPumpStroke.Suction => "drawing",
            ReciprocatingPumpStroke.Pressure => "pressurizing",
            _ => "dead-center"
        };
    }

    internal AssetLocation? BoundaryContentCode(BlockFacing face)
    {
        if (!CanConnect(face) || !HasContent) return null;
        if (face == OutputFace && CurrentStroke == ReciprocatingPumpStroke.Pressure) return contentCode;
        if (face == InputFace && CurrentStroke == ReciprocatingPumpStroke.Suction) return contentCode;
        return null;
    }

    internal double BoundaryPressureKPa(BlockFacing face)
    {
        return CanConnect(face)
            ? ReciprocatingPumpMath.DrivingBoundaryPressureKPa(chamberPressureKPa, CurrentStroke, face == InputFace)
            : 0;
    }

    internal double Receive(
        AssetLocation incomingCode,
        double requestedLitres,
        double incomingTemperatureC)
    {
        if (!canWrite || CurrentStroke != ReciprocatingPumpStroke.Suction ||
            !PipeContent.IsValid(Api.World, incomingCode) ||
            (contentCode != null && !contentCode.Equals(incomingCode))) return 0;
        PipeContentPhase phase = PipeContent.Phase(Api.World, incomingCode);
        double room = phase == PipeContentPhase.Liquid
            ? Math.Max(0, chamberVolumeLitres - amountLitres)
            : double.PositiveInfinity;
        double received = Math.Min(
            Math.Max(0, double.IsFinite(requestedLitres) ? requestedLitres : 0), room);
        if (received <= 0) return 0;
        double energy = amountLitres * temperatureC + received * incomingTemperatureC;
        amountLitres += received;
        temperatureC = energy / amountLitres;
        contentCode ??= incomingCode.Clone();
        throughputLitresPerSecond += received / transferStepSeconds;
        intakeOpen = true;
        chamberPressureKPa = PressureAt(chamberVolumeLitres);
        return received;
    }

    internal double Provide(AssetLocation requestedCode, double requestedLitres)
    {
        if (!canWrite || CurrentStroke != ReciprocatingPumpStroke.Pressure ||
            contentCode?.Equals(requestedCode) != true) return 0;
        double provided = Math.Min(amountLitres,
            Math.Max(0, double.IsFinite(requestedLitres) ? requestedLitres : 0));
        if (provided <= 0) return 0;
        amountLitres -= provided;
        throughputLitresPerSecond += provided / transferStepSeconds;
        outputOpen = true;
        if (amountLitres == 0)
        {
            amountLitres = 0;
            contentCode = null;
        }
        chamberPressureKPa = PressureAt(chamberVolumeLitres);
        return provided;
    }

    internal void FinishSimulation(bool inputConnected, bool outputConnected)
    {
        if (!canWrite) return;
        chamberPressureKPa = PressureAt(chamberVolumeLitres);
        if (CurrentStroke == ReciprocatingPumpStroke.Suction && !inputConnected)
            statusCode = "input-disconnected";
        else if (CurrentStroke == ReciprocatingPumpStroke.Pressure && !outputConnected)
            statusCode = "output-blocked";
        else if (CurrentStroke == ReciprocatingPumpStroke.Pressure &&
                 chamberPressureKPa >= ReciprocatingPumpMath.ServicePressureKPa)
            statusCode = "output-overpressure";
    }

    public float GetMechanicalResistance(double crankAngleRadians, double travel = .01, double seconds = .1)
    {
        if (!canWrite) return ReciprocatingPumpMath.BaseMechanicalResistance;
        double room = ValidPipe(outputPipe, OutputFace)
            ? UsesNetworkSolver ? double.PositiveInfinity : Math.Max(0, OutputCapacity() - outputPipe!.ContentAmountLitres) : 0;
        return ReciprocatingPumpMath.SampleStrokeLoad(crankAngleRadians, travel, amountLitres,
            temperatureC, ContentPhase, TransferPressure(outputPipe, outputPressure), room, seconds);
    }

    internal void ClearTransferPorts() { inputPipe = null; outputPipe = null; }

    internal void SetTransferPort(BlockFacing face, BlockEntityFluidPipe pipe, double pressure)
    {
        if (face == InputFace) { inputPipe = pipe; inputPressure = pressure; }
        if (face == OutputFace) { outputPipe = pipe; outputPressure = pressure; }
    }

    private bool ValidPipe(BlockEntityFluidPipe? pipe, BlockFacing face) => canWrite && pipe != null &&
        pipe.CanWriteState && pipe.CanConnect(face.Opposite) &&
        Api.World.BlockAccessor.GetChunkAtBlockPos(pipe.Pos) != null &&
        ReferenceEquals(Api.World.BlockAccessor.GetBlockEntity(Pos.AddCopy(face)), pipe) &&
        (contentCode == null || pipe.CurrentContentCode == null || contentCode.Equals(pipe.CurrentContentCode));

    private double OutputCapacity() => ContentPhase == PipeContentPhase.Gas ||
        (outputPipe is not BlockEntityIrrigatorPipe && outputPipe != null &&
         (outputPipe.IsPortEnabled(BlockFacing.UP) || outputPipe.GetAddon(BlockFacing.UP) == HydraulicFaceAddon.PipeNozzle) &&
         Api.World.BlockAccessor.GetBlock(outputPipe.Pos.UpCopy()).BlockMaterial == EnumBlockMaterial.Air)
            ? double.PositiveInfinity : HydraulicMath.PipeCapacityLitres;

    private double TransferPressure(BlockEntityFluidPipe? pipe, double cachedLiquidPressure) =>
        pipe != null && UsesNetworkSolver
            ? HydraulicMath.StoredPressure(pipe.ContentAmountLitres, pipe.ContentTemperatureC, ContentPhase, pipe.DrivenSuctionKPa)
            : pipe != null && (ContentPhase == PipeContentPhase.Gas || PipeContent.IsSteam(pipe.CurrentContentCode))
            ? HydraulicMath.GasGaugePressure(pipe.ContentAmountLitres, pipe.ContentTemperatureC)
            : cachedLiquidPressure;

    internal void StepDrivenPump(double seconds)
    {
        if (UsesNetworkSolver) return;
        PrepareSimulation(seconds);
        if (CurrentStroke == ReciprocatingPumpStroke.Suction && ValidPipe(inputPipe, InputFace) &&
            inputPipe!.CurrentContentCode is AssetLocation incoming)
        {
            double wanted = HydraulicMath.BoundedTransferLitres(TransferPressure(inputPipe, inputPressure) - chamberPressureKPa,
                inputPipe.ContentAmountLitres, amountLitres,
                PipeContent.IsSteam(incoming) ? double.PositiveInfinity : chamberVolumeLitres,
                seconds, PipeContent.Phase(Api.World, incoming));
            double received = Receive(incoming, wanted, inputPipe.ContentTemperatureC);
            if (received > 0) inputPipe.CommitPumpTransfer(incoming, inputPipe.ContentAmountLitres - received,
                inputPipe.ContentTemperatureC, received / seconds, InputFace.Opposite);
        }
        if (CurrentStroke == ReciprocatingPumpStroke.Pressure && HasContent && ValidPipe(outputPipe, OutputFace))
        {
            double pressure = TransferPressure(outputPipe, outputPressure);
            double wanted = Math.Min(amountLitres, Math.Min(
                Math.Max(0, OutputCapacity() - outputPipe!.ContentAmountLitres),
                ReciprocatingPumpMath.RequestedDischargeLitres(chamberPressureKPa - pressure, seconds, ContentPhase)));
            wanted = Math.Min(wanted, ReciprocatingPumpMath.DischargeToEquilibriumLitres(
                amountLitres, temperatureC, ContentPhase, chamberVolumeLitres, pressure));
            AssetLocation outgoing = contentCode!;
            double sent = Provide(outgoing, wanted);
            if (sent > 0)
            {
                double total = outputPipe.ContentAmountLitres + sent;
                double temperature = (outputPipe.ContentAmountLitres * outputPipe.ContentTemperatureC + sent * temperatureC) / total;
                outputPipe.CommitPumpTransfer(outgoing, total, temperature, sent / seconds, OutputFace);
            }
        }
        FinishSimulation(ValidPipe(inputPipe, InputFace), ValidPipe(outputPipe, OutputFace));
        AccumulatePresentation(seconds);
    }

    internal void AccumulatePresentation(double seconds)
    {
        frameSeconds += seconds;
        if (intakeOpen) frameIntakeLitres += throughputLitresPerSecond * seconds;
        if (outputOpen) frameOutputLitres += throughputLitresPerSecond * seconds;
    }

    internal PumpPresentation CapturePresentation(BEBehaviorMPLateralCrank crank)
    {
        PumpPresentation result = new()
        {
            X = Pos.X, Y = Pos.Y, Z = Pos.Z, Dimension = Pos.dimension,
            Angle = crank.PumpAngle(this), Ratio = crank.PumpTravel(this, 1), Amount = amountLitres,
            Content = contentCode?.ToString() ?? "", Temperature = temperatureC, Volume = chamberVolumeLitres,
            Throughput = frameSeconds > 0 ? (frameIntakeLitres + frameOutputLitres) / frameSeconds : 0,
            Stroke = (int)CurrentStroke,
            IntakeOpen = frameIntakeLitres > 0, OutputOpen = frameOutputLitres > 0
        };
        frameSeconds = frameIntakeLitres = frameOutputLitres = 0;
        return result;
    }

    public void SetNetworkState(
        AssetLocation? networkContentCode,
        double pressure,
        string networkStatusCode,
        BlockFacing? flowDirection)
    {
        statusCode = networkStatusCode;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        ITreeAttribute? state = tree.GetTreeAttribute(StorageKey);
        if (state == null)
        {
            preservedState = new TreeAttribute();
            canWrite = true;
            return;
        }
        int? schema = state.TryGetInt("schemaVersion");
        preservedState = ReciprocatingPumpStateSchema.PrepareForRead(
            state, out canWrite, out string? problem);
        DriveFace = SafeFace(preservedState.GetString("driveFace", "up"), BlockFacing.UP);
        OutputFace = SafeFace(preservedState.GetString("outputFace", "east"), BlockFacing.EAST);
        if (DriveFace.Axis == OutputFace.Axis) OutputFace = DriveFace.Axis == EnumAxis.X
            ? BlockFacing.SOUTH : BlockFacing.EAST;
        renderer?.RefreshOrientation();
        string storedContent = preservedState.GetString("contentCode", "");
        try { contentCode = string.IsNullOrWhiteSpace(storedContent) ? null : new AssetLocation(storedContent); }
        catch { contentCode = null; }
        amountLitres = Math.Max(0, preservedState.GetDouble("amountLitres", 0));
        temperatureC = preservedState.GetDouble("temperatureC", 20);
        chamberVolumeLitres = Math.Clamp(
            preservedState.GetDouble("lastVolumeLitres", ReciprocatingPumpMath.ChamberVolumeLitres(0)),
            ReciprocatingPumpMath.ClearanceVolumeLitres,
            ReciprocatingPumpMath.ClearanceVolumeLitres + ReciprocatingPumpMath.StrokeCapacityLitres);
        previousVolumeLitres = chamberVolumeLitres;
        chamberPressureKPa = PressureAt(chamberVolumeLitres);
        statusCode = !canWrite ? "state-read-only" :
            chamberPressureKPa >= ReciprocatingPumpMath.ServicePressureKPa ? "output-overpressure" : "idle";
        if (!canWrite)
        {
            worldAccessForResolve.Logger.Error(
                "[Gearwright] Reciprocating pump state at {0} has a {1} schema ({2}). The original data will remain read-only and pumping is disabled.",
                Pos, problem, schema?.ToString() ?? "non-integer");
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (!canWrite)
        {
            tree[StorageKey] = preservedState.Clone();
            return;
        }
        ITreeAttribute state = preservedState.Clone();
        state.SetInt("schemaVersion", ReciprocatingPumpStateSchema.CurrentVersion);
        state.SetString("driveFace", DriveFace.Code);
        state.SetString("outputFace", OutputFace.Code);
        if (contentCode == null) state.RemoveAttribute("contentCode");
        else state.SetString("contentCode", contentCode.ToString());
        state.SetDouble("amountLitres", amountLitres);
        state.SetDouble("temperatureC", temperatureC);
        state.SetDouble("lastVolumeLitres", chamberVolumeLitres);
        preservedState = state.Clone();
        tree[StorageKey] = state;
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-input", Lang.Get("direction-" + InputFace.Code)));
        dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-output", Lang.Get("direction-" + OutputFace.Code)));
        dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-amount", amountLitres));
        dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-pressure", chamberPressureKPa));
        dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-status-" + statusCode));
        if (!canWrite) dsc.AppendLine(Lang.Get("gearwright:reciprocating-pump-state-read-only"));
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        string shapeCode = HasDownwardStandSupport()
            ? "gearwright:shapes/block/reciprocating-pump-body-supported.json"
            : "gearwright:shapes/block/reciprocating-pump-body.json";
        Shape shape = Shape.TryGet(Api, shapeCode);
        tesselator.TesselateShape(Block, shape, out MeshData mesh, new Vec3f());
        mesh.MatrixTransform(PumpOrientation.Matrix(OutputFace, DriveFace));
        mesher.AddMeshData(mesh, 1);
        return true;
    }

    public override void OnBlockRemoved()
    {
        Shutdown();
        base.OnBlockRemoved();
    }

    public override void OnBlockUnloaded()
    {
        Shutdown();
        base.OnBlockUnloaded();
    }

    internal BEBehaviorMPLateralCrank? FindCrank()
    {
        BEBehaviorMPLateralCrank? drive = Api?.World.BlockAccessor
            .GetBlockEntity(Pos.AddCopy(DriveFace))?
            .GetBehavior<BEBehaviorMPLateralCrank>();
        return drive != null && drive.AxisFaces()[0].Axis == OutputFace.Axis
            ? drive
            : null;
    }

    private double PressureAt(double volume)
    {
        return ReciprocatingPumpMath.ChamberPressureKPa(
            amountLitres, temperatureC, ContentPhase, volume);
    }

    private bool HasDownwardStandSupport()
    {
        if (DriveFace != BlockFacing.UP || Api == null) return false;
        Block below = Api.World.BlockAccessor.GetBlock(Pos.DownCopy());
        return below.SideSolid[BlockFacing.UP.Index];
    }

    private static BlockFacing SafeFace(string code, BlockFacing fallback) =>
        BlockFacing.FromCode(code) ?? fallback;

    private void Shutdown()
    {
        if (registered && Api?.Side == EnumAppSide.Server)
        {
            Api.ModLoader.GetModSystem<HydraulicNetworkSystem>().UnregisterPump(this);
            registered = false;
        }
        if (renderer != null && Api is ICoreClientAPI capi)
        {
            capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
            renderer.Dispose();
            renderer = null;
        }
    }
}
