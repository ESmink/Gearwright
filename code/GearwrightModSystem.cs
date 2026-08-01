using Gearwright.Storage;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Gearwright;

/// <summary>Owns Gearwright's stable world data and status command.</summary>
public sealed class GearwrightModSystem : ModSystem
{
    public const string ModId = "gearwright";
    public const string ModVersion = "0.1.0";
    public const string WorldStateStorageKey = "gearwright:world-state";

    private ICoreServerAPI? serverApi;
    private GearwrightWorldState? worldState;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

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
