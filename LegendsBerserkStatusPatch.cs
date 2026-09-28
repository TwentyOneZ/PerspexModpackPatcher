using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsBerserkStatusPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Constructor(typeof(SE_Berserk)),
            postfix: new HarmonyMethod(typeof(LegendsBerserkStatusPatch), nameof(Defaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Berserk), nameof(SE_Berserk.ModifySpeed)),
            prefix: new HarmonyMethod(typeof(LegendsBerserkStatusPatch), nameof(Speed)));
        harmony.Patch(AccessTools.Method(typeof(SE_Berserk), nameof(SE_Berserk.UpdateStatusEffect)),
            transpiler: new HarmonyMethod(typeof(LegendsBerserkStatusPatch), nameof(TickCalls)));
        harmony.Patch(AccessTools.Method(typeof(SE_Berserk), nameof(SE_Berserk.IsDone)),
            prefix: new HarmonyMethod(typeof(LegendsBerserkStatusPatch), nameof(Done)));
        harmony.Patch(AccessTools.Method(typeof(SE_Berserk), nameof(SE_Berserk.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsBerserkStatusPatch), nameof(CanAdd)));
    }

    private static void Defaults(SE_Berserk __instance)
    {
        __instance.speedModifier = 1.2f;
        __instance.damageModifier = 1.2f;
        __instance.healthAbsorbPercent = 0.15f;
        __instance.m_tooltip = "Drains health to boost damage and speed. Restores stamina from damage dealt.";
    }

    private static bool Speed(SE_Berserk __instance, ref float speed)
    {
        speed *= __instance.speedModifier;
        return false;
    }

    private static IEnumerable<CodeInstruction> TickCalls(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.ApplyDamage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsBerserkStatusPatch), nameof(Drain));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Dekas Berserk drain calls: {count}");
    }

    private static void Drain(Character character, HitData hit, bool text, bool effects,
        HitData.DamageModifier modifier)
    {
        if (character.GetHealth() < Mathf.Clamp(0.10f * character.GetMaxHealth(), 5f, 30f))
        {
            var berserk = character.GetSEMan().GetStatusEffect("SE_VL_Berserk".GetStableHashCode());
            if (berserk != null) character.GetSEMan().RemoveStatusEffect(berserk, true);
            character.Message(MessageHud.MessageType.Center, "Low health!");
            character.Message(MessageHud.MessageType.TopLeft, "Berserk dissipated due to low health!");
            return;
        }
        character.ApplyDamage(hit, text, effects, modifier);
    }

    private static bool Done(SE_Berserk __instance, ref bool __result)
    {
        if (ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Berserker)
            return true;
        var player = __instance.m_character as Player;
        if (player != null)
        {
            var saved = AccessTools.Field(typeof(SE_Berserk), "savedStaminaRegenDelay").GetValue(__instance);
            AccessTools.Field(typeof(Player), "m_staminaRegenDelay").SetValue(player, saved);
        }
        __result = true;
        return false;
    }

    private static void CanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Berserker;
}
