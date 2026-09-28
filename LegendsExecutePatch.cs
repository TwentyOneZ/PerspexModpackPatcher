using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsExecutePatch
{
    private const short Marker = 0x4000;

    internal static void Install(Harmony harmony)
    {
        var nested = typeof(ValheimLegends.ValheimLegends).GetNestedType("VL_Damage_Patch",
            BindingFlags.Public | BindingFlags.NonPublic);
        harmony.Patch(AccessTools.Method(nested, "Prefix"),
            transpiler: new HarmonyMethod(typeof(LegendsExecutePatch), nameof(FilterExecute)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage)),
            prefix: new HarmonyMethod(typeof(LegendsExecutePatch), nameof(MarkExecute)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Character), "RPC_Damage"),
            prefix: new HarmonyMethod(typeof(LegendsExecutePatch), nameof(Threshold)) { priority = Priority.First });
    }

    private static IEnumerable<CodeInstruction> FilterExecute(IEnumerable<CodeInstruction> source)
    {
        var lastString = false;
        var replacements = 0;
        foreach (var instruction in source)
        {
            if (instruction.opcode == OpCodes.Ldstr)
                lastString = Equals(instruction.operand, "SE_VL_Execute");
            if (lastString && instruction.operand is MethodInfo method && method.DeclaringType == typeof(SEMan) &&
                method.Name == nameof(SEMan.HaveStatusEffect))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsExecutePatch), nameof(HasExecute));
                lastString = false;
                replacements++;
            }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException($"Unexpected Dekas Execute checks: {replacements}");
    }

    private static bool HasExecute(SEMan effects, int hash, ref HitData hit) =>
        IsWeaponHit(hit) && effects.HaveStatusEffect(hash);

    private static bool IsWeaponHit(HitData hit)
    {
        var type = hit.m_skill;
        return hit.m_damage.m_damage + hit.m_damage.m_blunt + hit.m_damage.m_pierce + hit.m_damage.m_slash > 0f &&
               type != ValheimLegends.ValheimLegends.EvocationSkill &&
               type != ValheimLegends.ValheimLegends.AlterationSkill &&
               type != ValheimLegends.ValheimLegends.ConjurationSkill &&
               type != ValheimLegends.ValheimLegends.IllusionSkill &&
               type != ValheimLegends.ValheimLegends.AbjurationSkill;
    }

    private static void MarkExecute(ref HitData hit)
    {
        if (!IsWeaponHit(hit) || hit.GetAttacker() is not Player attacker ||
            !attacker.GetSEMan().HaveStatusEffect("SE_VL_Execute".GetStableHashCode())) return;
        hit.m_toolTier = (short)(hit.m_toolTier | Marker);
    }

    private static void Threshold(Character __instance, HitData hit)
    {
        if ((hit.m_toolTier & Marker) == 0) return;
        hit.m_toolTier = (short)(hit.m_toolTier & ~Marker);
        if (__instance.GetHealth() < __instance.GetMaxHealth() * 0.2f)
            hit.m_damage.Modify(2f);
    }
}
