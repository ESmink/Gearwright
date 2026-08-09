using Vintagestory.API.Common;

namespace Gearwright.Hydraulics;

/// <summary>Resolves the content identities accepted by Gearwright pipes.</summary>
public static class PipeContent
{
    private static readonly AssetLocation SteamCode = new(HydraulicCodes.Steam);

    public static bool IsSteam(AssetLocation? code) => code?.Equals(SteamCode) == true;

    public static bool IsValid(IWorldAccessor world, AssetLocation code) =>
        IsSteam(code) || IsVintageStoryLiquid(world, code);

    public static bool IsVintageStoryLiquid(IWorldAccessor world, AssetLocation code)
    {
        Item? item = world.GetItem(code);
        return item?.Attributes?["waterTightContainerProps"].Exists == true;
    }

    public static PipeContentPhase Phase(IWorldAccessor world, AssetLocation code) =>
        IsSteam(code) ? PipeContentPhase.Gas : PipeContentPhase.Liquid;

    public static double DefaultTemperatureC(AssetLocation code) => IsSteam(code) ? 100 : 20;

    public static AssetLocation SteamTexture => new("gearwright:block/steam");
}
