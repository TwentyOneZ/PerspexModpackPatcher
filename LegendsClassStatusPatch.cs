using System;
using System.Runtime.CompilerServices;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsClassStatusPatch
{
    private sealed class ChargeTimer { internal float Remaining; internal bool Refreshed; }
    private static readonly ConditionalWeakTable<SE_Valkyrie, ChargeTimer> Timers = new();
    private static readonly ConditionalWeakTable<SE_Rogue, ChargeTimer> RogueTimers = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SE_Ranger), nameof(SE_Ranger.ModifyRunStaminaDrain)),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(RangerRun)));
        harmony.Patch(AccessTools.Method(typeof(SE_Rogue), nameof(SE_Rogue.ModifySpeed)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(RogueSpeed)));
        harmony.Patch(AccessTools.Method(typeof(SE_Rogue), nameof(SE_Rogue.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(BeforeRogueTick)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(AfterRogueTick)));
        harmony.Patch(AccessTools.Method(typeof(SE_Monk), nameof(SE_Monk.ModifySpeed)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(MonkSpeed)));
        harmony.Patch(AccessTools.Method(typeof(SE_Valkyrie), nameof(SE_Valkyrie.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(BeforeValkyrieTick)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(AfterValkyrieTick)));
        harmony.Patch(AccessTools.Method(typeof(SE_Bulwark), nameof(SE_Bulwark.OnDamaged)),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(BulwarkDamage)));
        harmony.Patch(AccessTools.Method(typeof(SE_Bulwark), nameof(SE_Bulwark.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(BulwarkCanAdd)));
        harmony.Patch(AccessTools.Method(typeof(SE_ShadowStalk), nameof(SE_ShadowStalk.ModifySpeed)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(ShadowSpeed)));
        harmony.Patch(AccessTools.Method(typeof(SE_ShadowStalk), nameof(SE_ShadowStalk.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(ShadowCanAdd)));
        harmony.Patch(AccessTools.Constructor(typeof(SE_Execute)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(ExecuteDefaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Execute), nameof(SE_Execute.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(ExecuteCanAdd)));
        harmony.Patch(AccessTools.Method(typeof(StatusEffect), nameof(StatusEffect.IsDone)),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(ClassStatusDone)));
        harmony.Patch(AccessTools.Method(typeof(Class_Valkyrie), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(BeforeValkyrieInput)),
            postfix: new HarmonyMethod(typeof(LegendsClassStatusPatch), nameof(AfterValkyrieInput)));
    }

    private static void RangerRun(SE_Ranger __instance, float baseDrain, ref float drain) =>
        drain += baseDrain * __instance.m_runStaminaDrainModifier;

    private static bool BulwarkDamage(SE_Bulwark __instance, HitData hit)
    {
        var player = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (player == null) return true;
        var attributes = LevelSystem.Instance;
        var bonus = attributes == null ? 0f : Mathf.Clamp(
            attributes.getAddHp() / 400f + attributes.getAddStamina() / 200f, 0f, 0.5f);
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill);
        hit.m_damage.Modify(Mathf.Clamp(0.75f - skill * (1f + bonus) / 200f, 0.75f, 0.25f) *
                            VL_GlobalConfigs.c_valkyrieBulwark);
        return false;
    }

    private static void BulwarkCanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Valkyrie;

    private static void ShadowSpeed(SE_ShadowStalk __instance, ref float speed)
    {
        var character = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (character != null && character.IsSneaking()) speed *= 1f + MartialModifier();
    }

    private static void ShadowCanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Ranger;

    private static void ExecuteDefaults(SE_Execute __instance)
    {
        __instance.staggerForce = 1.2f;
        __instance.damageBonus = 1.5f;
        __instance.hitCount = 3;
        __instance.m_tooltip = "Execute";
    }

    private static void ExecuteCanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Berserker;

    private static bool ClassStatusDone(StatusEffect __instance, ref bool __result)
    {
        var playerClass = ValheimLegends.ValheimLegends.vl_player?.vl_class;
        if (__instance is SE_Bulwark && playerClass == ValheimLegends.ValheimLegends.PlayerClass.Valkyrie ||
            __instance is SE_ShadowStalk && playerClass == ValheimLegends.ValheimLegends.PlayerClass.Ranger ||
            __instance is SE_Execute && playerClass == ValheimLegends.ValheimLegends.PlayerClass.Berserker) return true;
        if (__instance is not (SE_Bulwark or SE_ShadowStalk or SE_Execute))
            return true;
        __result = true;
        return false;
    }

    private static float MartialModifier()
    {
        var attributes = LevelSystem.Instance;
        return attributes == null ? 0f : Mathf.Clamp(
            attributes.getAddPhysicDamage() / 40f + attributes.getAddAttackSpeed() / 40f,
            0f, 0.5f);
    }

    private static void RogueSpeed(SE_Rogue __instance, ref float speed)
    {
        var player = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (player == null || !player.IsSneaking()) return;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        speed *= (1.5f + 0.01f * skill * (1f + MartialModifier())) /
                 (1.5f + 0.01f * skill);
    }

    private static void MonkSpeed(SE_Monk __instance, ref float speed)
    {
        if (!__instance.surging) return;
        var player = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (player == null) return;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        speed *= (1.2f + 0.003f * skill * (1f + MartialModifier())) /
                 (1.2f + 0.003f * skill);
    }

    private static void BeforeRogueTick(SE_Rogue __instance) =>
        Traverse.Create(__instance).Field("m_timer").SetValue(float.PositiveInfinity);

    private static void AfterRogueTick(SE_Rogue __instance, float dt)
    {
        var state = RogueTimers.GetOrCreateValue(__instance);
        state.Remaining -= dt;
        if (state.Remaining <= 0f)
        {
            var player = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
            var skill = player?.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill) ?? 0f;
            var max = 1 + Mathf.RoundToInt(skill * 0.1f * (1f + MartialModifier()));
            __instance.hitCount = Mathf.Clamp(__instance.hitCount + 1, 0, max);
            state.Remaining = 20f * VL_GlobalConfigs.c_rogueTrickCharge;
        }
        __instance.m_ttl = __instance.hitCount;
        Traverse.Create(__instance).Field("m_time").SetValue(0f);
    }

    private static void BeforeValkyrieTick(SE_Valkyrie __instance)
    {
        // Keep the published update's base StatusEffect call while handling the local charge curve below.
        Traverse.Create(__instance).Field("m_timer").SetValue(float.PositiveInfinity);
    }

    private static void AfterValkyrieTick(SE_Valkyrie __instance, float dt)
    {
        var state = Timers.GetOrCreateValue(__instance);
        var interval = 15f * VL_GlobalConfigs.c_valkyrieChargeDuration;
        if (state.Refreshed)
        {
            state.Remaining = interval;
            state.Refreshed = false;
        }
        state.Remaining -= dt;
        if (state.Remaining <= 0f)
        {
            var player = Traverse.Create(__instance).Field("m_character").GetValue<Character>() as Player;
            var skill = player?.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill) ?? 0f;
            var modifier = MartialModifier();
            var max = 8 + Mathf.RoundToInt(Mathf.Sqrt(skill * (1f + modifier)));
            __instance.hitCount = Mathf.Clamp(__instance.hitCount - 1, 0, max);
            state.Remaining = interval;
        }
        __instance.m_ttl = __instance.hitCount;
        Traverse.Create(__instance).Field("m_time").SetValue(0f);
    }

    private static void BeforeValkyrieInput(Player player, out int __state)
    {
        __state = (player.GetSEMan().GetStatusEffect("SE_VL_Valkyrie".GetStableHashCode())
            as SE_Valkyrie)?.hitCount ?? 0;
    }

    private static void AfterValkyrieInput(Player player, int __state)
    {
        if (!player.IsBlocking() || !ZInput.GetButtonDown("Attack")) return;
        var status = player.GetSEMan().GetStatusEffect("SE_VL_Valkyrie".GetStableHashCode()) as SE_Valkyrie;
        if (status != null && status.hitCount < __state)
            Timers.GetOrCreateValue(status).Refreshed = true;
    }
}
