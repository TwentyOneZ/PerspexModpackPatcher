using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMinorStatusPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Constructor(typeof(SE_Weaken)),
            postfix: new HarmonyMethod(typeof(LegendsMinorStatusPatch), nameof(WeakenDefaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Weaken), nameof(SE_Weaken.OnDamaged)),
            transpiler: new HarmonyMethod(typeof(LegendsMinorStatusPatch), nameof(WeakenCalls)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsMinorStatusPatch), nameof(WeakenVictim)) { priority = Priority.First });
        harmony.Patch(AccessTools.Constructor(typeof(SE_PowerShot)),
            postfix: new HarmonyMethod(typeof(LegendsMinorStatusPatch), nameof(PowerShotDefaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_PowerShot), nameof(SE_PowerShot.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsMinorStatusPatch), nameof(PowerShotCanAdd)));
    }

    private static void WeakenDefaults(SE_Weaken __instance) => __instance.damageReduction = 0.15f;

    private static IEnumerable<CodeInstruction> WeakenCalls(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.AddStamina))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMinorStatusPatch), nameof(NoStaminaRefund));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Dekas Weaken refund calls: {count}");
    }

    private static void NoStaminaRefund(Character target, float amount) { }

    private static void WeakenVictim(Character __instance, HitData hit)
    {
        if (hit?.GetAttacker() is not Player attacker ||
            __instance.GetSEMan()?.GetStatusEffect("SE_VL_Weaken".GetStableHashCode()) is not SE_Weaken weaken)
            return;
        attacker.AddStamina(5f + hit.GetTotalDamage() * weaken.staminaDrain);
    }

    private static void PowerShotDefaults(SE_PowerShot __instance)
    {
        __instance.hitCount = (int)__instance.m_ttl;
        __instance.m_tooltip = "Power Shot";
    }

    private static void PowerShotCanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Ranger;
}
