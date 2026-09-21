using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Gearwright.Mechanics;

/// <summary>Retains vanilla behavior when the bellows has no Gearwright crank.</summary>
public sealed class LargeBellowsSystem : ModSystem
{
    private const string PatchId = "gearwright.large-bellows";
    private static readonly object Gate = new();
    private static Harmony? harmony;
    private static int users;
    private bool registered;
    private static readonly FieldInfo animation = AccessTools.Field(typeof(BlockEntityMechPoweredBellows), "animUtil");
    private static readonly FieldInfo connected = AccessTools.Field(typeof(BlockEntityMechPoweredBellows), "connected");

    public override void Start(ICoreAPI api)
    {
        lock (Gate)
        {
            if (users == 0)
            {
                Harmony patcher = new(PatchId);
                try
                {
                    patcher.Patch(AccessTools.Method(typeof(BlockEntityMechPoweredBellows), "onTick"),
                        prefix: new HarmonyMethod(typeof(LargeBellowsSystem), nameof(BeforeTick)),
                        postfix: new HarmonyMethod(typeof(LargeBellowsSystem), nameof(AfterTick)));
                    patcher.Patch(AccessTools.Method(typeof(BlockEntityMechPoweredBellows), "OnTesselation"),
                        prefix: new HarmonyMethod(typeof(LargeBellowsSystem), nameof(BeforeTesselation)));
                    harmony = patcher;
                }
                catch { patcher.UnpatchAll(PatchId); throw; }
            }
            users++; registered = true;
        }
    }

    private static bool BeforeTick(BlockEntityMechPoweredBellows __instance)
    {
        if (__instance.GetBehavior<BEBehaviorLargeBellows>()?.SelectedDrive() == null) return true;
        if ((bool)connected.GetValue(__instance)!)
        {
            if (__instance.Api is ICoreClientAPI client) client.Event.UnregisterRenderer(__instance, EnumRenderStage.Opaque);
            connected.SetValue(__instance, false);
            (animation.GetValue(__instance) as BlockEntityAnimationUtil)?.StopAnimation("bellowing");
        }
        HideLegacyAnimation(__instance);
        return false;
    }

    internal static void HideLegacyAnimation(BlockEntity entity)
    {
        if (entity is BlockEntityMechPoweredBellows && animation.GetValue(entity) is BlockEntityAnimationUtil util && util.renderer != null)
            util.renderer.ShouldRender = false;
    }

    private static void AfterTick(BlockEntityMechPoweredBellows __instance)
    {
        if (__instance.GetBehavior<BEBehaviorLargeBellows>() is { } bellows && bellows.SelectedDrive() == null &&
            (bool)connected.GetValue(__instance)! && animation.GetValue(__instance) is BlockEntityAnimationUtil util && util.renderer != null)
            util.renderer.ShouldRender = true;
    }

    private static bool BeforeTesselation(BlockEntityMechPoweredBellows __instance, ref bool __result)
    {
        if (__instance.GetBehavior<BEBehaviorLargeBellows>()?.SelectedDrive() == null) return true;
        __result = true;
        return false;
    }

    public override void Dispose()
    {
        lock (Gate)
        {
            if (!registered) return;
            registered = false;
            if (--users == 0) { harmony?.UnpatchAll(PatchId); harmony = null; }
        }
    }
}
