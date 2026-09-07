using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Gearwright.Hydraulics;

public enum HydraulicFaceAddon
{
    None = 0,
    GlassWindow = 1,
    Sprinkler = 2,
    PipeNozzle = 3,
    CopperFlange = 4
}

public readonly record struct PumpOffer(
    BlockEntityHydraulicPump Pump,
    AssetLocation ContentCode,
    double Pressure,
    string StatusCode);

public interface IHydraulicNetworkNode
{
    BlockPos Position { get; }
    bool CanConnect(BlockFacing face);
    void SetNetworkState(
        AssetLocation? contentCode,
        double pressure,
        string statusCode,
        BlockFacing? flowDirection);
}

public static class HydraulicCodes
{
    public const string PipeBlock = "fluid-pipe-copper";
    public const string SprinklerItem = "sprinkler-brass";
    public const string PipeNozzleItem = "fluid-pipe-intake-copper";
    public const string IrrigatorPipeBlock = "irrigator-pipe-bronze";
    public const string CreativePumpBlock = "creative-fluid-pump";
    public const string PassivePumpBlock = "passive-fluid-pump";
    public const string ReciprocatingPumpBlock = "reciprocating-pump";

    public const string PipeClass = "GearwrightFluidPipe";
    public const string PipeEntityClass = "GearwrightFluidPipeEntity";
    public const string IrrigatorPipeClass = "GearwrightIrrigatorPipe";
    public const string IrrigatorPipeEntityClass = "GearwrightIrrigatorPipeEntity";
    public const string CreativePumpClass = "GearwrightCreativeFluidPump";
    public const string CreativePumpEntityClass = "GearwrightCreativeFluidPumpEntity";
    public const string PassivePumpClass = "GearwrightPassiveFluidPump";
    public const string PassivePumpEntityClass = "GearwrightPassiveFluidPumpEntity";
    public const string ReciprocatingPumpClass = "GearwrightReciprocatingPump";
    public const string ReciprocatingPumpEntityClass = "GearwrightReciprocatingPumpEntity";
    public const string CropBehaviorClass = "GearwrightFluidExposure";

    public const string FreshWater = "game:waterportion";
    public const string Steam = "gearwright:steam";
    public const string FarmlandExposureTree = "gearwrightFluidExposure";
}
