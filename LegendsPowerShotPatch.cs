using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsPowerShotPatch
{
    [System.ThreadStatic] private static bool pending;
    [System.ThreadStatic] private static float baseDamage;
    [System.ThreadStatic] private static float baseVelocity;

    internal static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Attack), "FireProjectileBurst");
        harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(LegendsPowerShotPatch), nameof(Remember)) { priority = Priority.First });
        harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(LegendsPowerShotPatch), nameof(Scale)) { priority = Priority.Last });
    }

    private static void Remember(Humanoid ___m_character, float ___m_damageMultiplier, float ___m_projectileVel)
    {
        pending = ___m_character is Player player &&
                  player.GetSEMan().HaveStatusEffect("SE_VL_PowerShot".GetStableHashCode());
        if (!pending) return;
        baseDamage = ___m_damageMultiplier;
        baseVelocity = ___m_projectileVel;
    }

    private static void Scale(Humanoid ___m_character, ref float ___m_damageMultiplier, ref float ___m_projectileVel)
    {
        if (!pending) return;
        pending = false;
        var player = (Player)___m_character;
        var school = Mathf.Clamp(player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill), 0f, 100f);
        var agility = Mathf.Clamp(LevelSystem.Instance.getParameter(Parameter.Agility), 0f, 100f);
        ___m_damageMultiplier = baseDamage * (0.75f + 0.005f * school) *
            (0.75f + 0.005f * agility) * 1.15f *
            VL_GlobalConfigs.g_DamageModifer * VL_GlobalConfigs.c_rangerPowerShot;
        ___m_projectileVel = baseVelocity * 2f;
    }
}
