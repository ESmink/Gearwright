using Gearwright.Storage;
using Gearwright.Hydraulics;
using Gearwright.Mechanics;
using System;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Gearwright;

/// <summary>Registers Gearwright and owns its stable world data and status command.</summary>
public sealed class GearwrightModSystem : ModSystem
{
    public const string ModId = "gearwright";
    public const string ModVersion = "0.1.0";
    public const string WorldStateStorageKey = "gearwright:world-state";

    private ICoreServerAPI? serverApi;
    private GearwrightWorldState? worldState;

    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass(HydraulicCodes.PipeClass, typeof(BlockFluidPipe));
        api.RegisterBlockEntityClass(HydraulicCodes.PipeEntityClass, typeof(BlockEntityFluidPipe));
        api.RegisterBlockClass(HydraulicCodes.IrrigatorPipeClass, typeof(BlockIrrigatorPipe));
        api.RegisterBlockEntityClass(HydraulicCodes.IrrigatorPipeEntityClass, typeof(BlockEntityIrrigatorPipe));
        api.RegisterBlockClass(HydraulicCodes.CreativePumpClass, typeof(BlockCreativeFluidPump));
        api.RegisterBlockEntityClass(HydraulicCodes.CreativePumpEntityClass, typeof(BlockEntityCreativeFluidPump));
        api.RegisterBlockClass(HydraulicCodes.PassivePumpClass, typeof(BlockPassiveFluidPump));
        api.RegisterBlockEntityClass(HydraulicCodes.PassivePumpEntityClass, typeof(BlockEntityPassiveFluidPump));
        api.RegisterCropBehavior(HydraulicCodes.CropBehaviorClass, typeof(CropBehaviorFluidExposure));
        api.RegisterBlockClass(MechanicalCodes.FlywheelBlockClass, typeof(BlockSmallFlywheel));
        api.RegisterBlockEntityBehaviorClass(
            MechanicalCodes.FlywheelBehaviorClass,
            typeof(BEBehaviorMPSmallFlywheel));
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        foreach (Block block in api.World.Blocks.Where(block => block?.CropProps != null))
        {
            if (block.CropProps.Behaviors?.Any(behavior => behavior is CropBehaviorFluidExposure) == true) continue;
            CropBehavior[] existing = block.CropProps.Behaviors ?? Array.Empty<CropBehavior>();
            block.CropProps.Behaviors = existing.Append(new CropBehaviorFluidExposure(block)).ToArray();
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        serverApi = api;
        api.Event.SaveGameCreated += LoadWorldState;
        api.Event.SaveGameLoaded += LoadWorldState;
        api.Event.GameWorldSave += SaveWorldState;

        api.ChatCommands.Create("gearwright")
            .WithDescription("Show the Gearwright version and save status")
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(ShowStatus);
    }

    public override void Dispose()
    {
        if (serverApi == null) return;
        serverApi.Event.SaveGameCreated -= LoadWorldState;
        serverApi.Event.SaveGameLoaded -= LoadWorldState;
        serverApi.Event.GameWorldSave -= SaveWorldState;
        serverApi = null;
        worldState = null;
    }

    private void LoadWorldState()
    {
        if (serverApi == null || worldState != null) return;
        byte[]? stored = serverApi.WorldManager.SaveGame.GetData(WorldStateStorageKey);
        worldState = GearwrightWorldState.Load(stored, ModVersion);

        if (worldState.CanWrite)
        {
            serverApi.Logger.Notification("[Gearwright] {0}", worldState.Notice);
        }
        else
        {
            serverApi.Logger.Error("[Gearwright] {0}", worldState.Notice);
        }
    }

    private void SaveWorldState()
    {
        if (serverApi == null || worldState?.CanWrite != true) return;
        serverApi.WorldManager.SaveGame.StoreData(WorldStateStorageKey, worldState.Serialize(ModVersion));
    }

    private TextCommandResult ShowStatus(TextCommandCallingArgs _)
    {
        if (worldState == null)
        {
            return TextCommandResult.Success(
                $"Gearwright {ModVersion} is installed. World data is still loading.");
        }

        string writeStatus = worldState.CanWrite ? "Save writes are enabled." : "Save writes are disabled.";
        return TextCommandResult.Success(
            $"Gearwright {ModVersion} is installed. World schema: {worldState.SchemaVersion}. {writeStatus} {worldState.Notice}");
    }
}
