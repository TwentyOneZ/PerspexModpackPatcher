using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMonkSurgePatch
{
    private static readonly FieldInfo Timer = AccessTools.Field(typeof(SE_Monk), "m_SurgeTimer");
    private static readonly FieldInfo Interval = AccessTools.Field(typeof(SE_Monk), "m_SurgeInterval");
    private static readonly FieldInfo Max = AccessTools.Field(typeof(SE_Monk), "maxHitCount");
    private sealed class ChargeTimer { internal float Remaining; internal bool Refreshed; }
    private static readonly ConditionalWeakTable<SE_Monk, ChargeTimer> ChargeTimers = new();

    internal static void Install(Harmony harmony)
    {
        if (Timer == null || Interval == null || Max == null)
            throw new MissingFieldException("Dekas SE_Monk surge timer fields changed");
        harmony.Patch(AccessTools.Constructor(typeof(SE_Monk)),
            postfix: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(Defaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Monk), nameof(SE_Monk.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(BeforeTick)),
            postfix: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(AfterTick)),
            transpiler: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(TickCalls)));
        foreach (var method in new[] { "Process_Input", "Execute_Attack", "Impact_Effect" })
            harmony.Patch(AccessTools.Method(typeof(Class_Monk), method),
                prefix: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(BeforeAbility)),
                postfix: new HarmonyMethod(typeof(LegendsMonkSurgePatch), nameof(AfterAbility)));
    }

    private static void Defaults(SE_Monk __instance)
    {
        Timer.SetValue(__instance, 2f);
        Interval.SetValue(__instance, 2f);
    }

    private static IEnumerable<CodeInstruction> TickCalls(IEnumerable<CodeInstruction> source)
    {
        var heals = 0;
        var stamina = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character))
            {
                if (method.Name == nameof(Character.Heal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegendsMonkSurgePatch), nameof(Heal));
                    heals++;
                }
                else if (method.Name == nameof(Character.AddStamina))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegendsMonkSurgePatch), nameof(NoStamina));
                    stamina++;
                }
            }
            yield return instruction;
        }
        if (heals != 1 || stamina != 1)
            throw new InvalidOperationException($"Unexpected Dekas Monk surge calls: heal={heals}, stamina={stamina}");
    }

    private static void Heal(Character target, float original, bool showText)
    {
        var skill = target.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        target.Heal(LegendsEconomyPatch.Healing(1f, skill) * VL_GlobalConfigs.c_monkSurge, showText);
    }

    private static void NoStamina(Character target, float original) { }

    private static void BeforeTick(SE_Monk __instance, out int __state)
    {
        __state = __instance.hitCount;
        AccessTools.Field(typeof(SE_Monk), "m_timer").SetValue(__instance, float.PositiveInfinity);
    }

    private static SE_Monk Status(Player player) => player?.GetSEMan()?.GetStatusEffect(
        "SE_VL_Monk".GetStableHashCode()) as SE_Monk;

    internal static void Refresh(SE_Monk status) => ChargeTimers.GetOrCreateValue(status).Refreshed = true;

    internal static int ChargeCap(Player player, SE_Monk status)
    {
        var level = LevelSystem.Instance;
        var school = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var scale = 1f + Mathf.Clamp(level.getAddPhysicDamage() / 40f + level.getAddAttackSpeed() / 40f,
            0f, 0.5f);
        var cap = 8 + Mathf.RoundToInt(school * 0.2f * scale);
        Max.SetValue(status, cap);
        return cap;
    }

    private static void BeforeAbility(Player player, out int __state) =>
        __state = Status(player)?.hitCount ?? 0;

    private static void AfterAbility(Player player, int __state)
    {
        var status = Status(player);
        if (status != null && status.hitCount != __state)
            ChargeTimers.GetOrCreateValue(status).Refreshed = true;
    }

    private static void AfterTick(SE_Monk __instance, float dt, int __state)
    {
        var player = __instance.m_character as Player;
        if (player == null) return;
        var timer = ChargeTimers.GetOrCreateValue(__instance);
        if (__instance.surging && __instance.hitCount < __state) timer.Refreshed = true;
        var interval = 15f * VL_GlobalConfigs.c_monkChiDuration;
        if (timer.Refreshed)
        {
            timer.Remaining = interval;
            timer.Refreshed = false;
        }
        timer.Remaining -= dt;
        if (timer.Remaining > 0f)
        {
            __instance.m_ttl = __instance.hitCount;
            return;
        }
        var level = LevelSystem.Instance;
        var school = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var scale = 1f + Mathf.Clamp(level.getAddPhysicDamage() / 40f + level.getAddAttackSpeed() / 40f,
            0f, 0.5f);
        var cap = 8 + Mathf.RoundToInt(school * 0.2f * scale);
        Max.SetValue(__instance, cap);
        __instance.hitCount = Mathf.Clamp(__instance.hitCount - 1, 0, cap);
        __instance.m_ttl = __instance.hitCount;
        timer.Remaining = interval;
    }
}
