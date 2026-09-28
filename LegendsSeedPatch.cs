using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsSeedPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SE_SeedRegeneration), nameof(SE_SeedRegeneration.UpdateStatusEffect)),
            transpiler: new HarmonyMethod(typeof(LegendsSeedPatch), nameof(TickCalls)));
        harmony.Patch(AccessTools.Method(typeof(SE_SeedRegeneration), nameof(SE_SeedRegeneration.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsSeedPatch), nameof(CanAdd)));
        harmony.Patch(AccessTools.Method(typeof(StatusEffect), nameof(StatusEffect.IsDone)),
            prefix: new HarmonyMethod(typeof(LegendsSeedPatch), nameof(Done)));
    }

    private static IEnumerable<CodeInstruction> TickCalls(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.AddStamina))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsSeedPatch), nameof(Restore));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Dekas Seed Regeneration tick calls: {count}");
    }

    private static void Restore(Character target, float amount)
    {
        target.AddStamina(amount);
        target.AddEitr(amount);
    }

    private static void CanAdd(ref bool __result) =>
        __result &= ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                    ValheimLegends.ValheimLegends.PlayerClass.Druid;

    private static bool Done(StatusEffect __instance, ref bool __result)
    {
        var required = __instance is SE_SeedRegeneration
            ? ValheimLegends.ValheimLegends.PlayerClass.Druid
            : __instance is SE_PowerShot
                ? ValheimLegends.ValheimLegends.PlayerClass.Ranger
                : ValheimLegends.ValheimLegends.PlayerClass.None;
        if (__instance is SE_Riposte && ValheimLegends.ValheimLegends.vl_player?.vl_class is not
            (ValheimLegends.ValheimLegends.PlayerClass.Rogue or ValheimLegends.ValheimLegends.PlayerClass.Duelist))
        {
            __result = true;
            return false;
        }
        if (required == ValheimLegends.ValheimLegends.PlayerClass.None ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class == required) return true;
        __result = true;
        return false;
    }
}
