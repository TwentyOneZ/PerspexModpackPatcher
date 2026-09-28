using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsShamanStatusPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Constructor(typeof(SE_Shell)),
            postfix: new HarmonyMethod(typeof(LegendsShamanStatusPatch), nameof(ShellDefaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Shell), nameof(SE_Shell.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsShamanStatusPatch), nameof(ShellTick)));
        harmony.Patch(AccessTools.Constructor(typeof(SE_Enrage)),
            postfix: new HarmonyMethod(typeof(LegendsShamanStatusPatch), nameof(EnrageDefaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Enrage), nameof(SE_Enrage.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsShamanStatusPatch), nameof(EnrageTick)));
        harmony.Patch(AccessTools.Method(typeof(SE_Enrage), nameof(SE_Enrage.ModifySpeed)),
            prefix: new HarmonyMethod(typeof(LegendsShamanStatusPatch), nameof(EnrageSpeed)));
    }

    private static void ShellDefaults(SE_Shell __instance)
    {
        __instance.resistModifier = 0.6f;
        __instance.spiritDamageOffset = 6f;
        __instance.m_tooltip = "Shell";
    }

    private static void ShellTick(SE_Shell __instance)
    {
        if (!__instance.doOnce) return;
        __instance.doOnce = false;
        var scale = LevelSystem.Instance.getLevel() * 10f / 6f;
        __instance.m_ttl = SE_Shell.m_baseTTL + 0.3f * scale;
        __instance.spiritDamageOffset = (6f + 0.3f * scale) *
            VL_GlobalConfigs.g_DamageModifer * VL_GlobalConfigs.c_shamanShell;
        __instance.resistModifier = (0.6f - 0.006f * scale) * VL_GlobalConfigs.c_shamanShell;
    }

    private static void EnrageDefaults(SE_Enrage __instance)
    {
        __instance.speedModifier = 1.2f;
        __instance.staminaModifier = 10f;
        __instance.m_tooltip = "Enrage";
    }

    private static void EnrageTick(SE_Enrage __instance)
    {
        if (!__instance.doOnce) return;
        __instance.doOnce = false;
        var level = LevelSystem.Instance;
        var scale = level.getLevel() * 10f / 6f;
        __instance.m_ttl = 20f + 0.2f * scale;
        __instance.staminaModifier = (5f + 0.1f * scale) * VL_GlobalConfigs.c_shamanEnrage;
        __instance.speedModifier = 1.2f + 0.002f * scale *
            (1f + Mathf.Clamp(level.getAddHp() / 400f + level.getAddMagicDamage() / 80f, 0f, 0.5f));
    }

    private static bool EnrageSpeed(SE_Enrage __instance, ref float speed)
    {
        speed *= __instance.speedModifier;
        return false;
    }
}
