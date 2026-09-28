using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsRipostePatch
{
    [ThreadStatic] private static Player defender;
    [ThreadStatic] private static bool ready;
    [ThreadStatic] private static float pushForce;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Constructor(typeof(SE_Riposte)),
            postfix: new HarmonyMethod(typeof(LegendsRipostePatch), nameof(Defaults)));
        harmony.Patch(AccessTools.Method(typeof(SE_Riposte), nameof(SE_Riposte.CanAdd)),
            postfix: new HarmonyMethod(typeof(LegendsRipostePatch), nameof(CanAdd)));
        var type = typeof(ValheimLegends.ValheimLegends).GetNestedType("Block_Class_Patch",
            BindingFlags.Public | BindingFlags.NonPublic);
        var method = AccessTools.Method(type, "Prefix");
        if (method == null) throw new MissingMethodException("Dekas 0.7.10 block patch missing");
        harmony.Patch(method,
            prefix: new HarmonyMethod(typeof(LegendsRipostePatch), nameof(Before)),
            postfix: new HarmonyMethod(typeof(LegendsRipostePatch), nameof(After)),
            transpiler: new HarmonyMethod(typeof(LegendsRipostePatch), nameof(BlockCalls)));
    }

    private static bool HasClass() => ValheimLegends.ValheimLegends.vl_player?.vl_class is
        ValheimLegends.ValheimLegends.PlayerClass.Rogue or ValheimLegends.ValheimLegends.PlayerClass.Duelist;

    private static void Defaults(SE_Riposte __instance) => __instance.m_tooltip = "Riposte";

    private static void CanAdd(ref bool __result) => __result &= HasClass();

    private static void Before(Humanoid __0, HitData __1)
    {
        defender = __0 as Player;
        ready = defender != null && defender.GetSEMan().HaveStatusEffect("SE_VL_Riposte".GetStableHashCode());
        pushForce = __1.m_pushForce;
    }

    private static void After()
    {
        defender = null;
        ready = false;
    }

    private static IEnumerable<CodeInstruction> BlockCalls(IEnumerable<CodeInstruction> source)
    {
        var range = 0;
        var damage = 0;
        var level = 0;
        foreach (var instruction in source)
        {
            if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == 3f)
            {
                instruction.operand = 8f;
                range++;
            }
            else if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                     method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsRipostePatch), nameof(Damage));
                damage++;
            }
            else if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field &&
                     field.DeclaringType == typeof(Skills.Skill) && field.Name == "m_level")
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsRipostePatch), nameof(EffectiveLevel));
                level++;
            }
            yield return instruction;
        }
        if (range != 1 || damage != 1 || level != 1)
            throw new InvalidOperationException($"Unexpected Dekas Riposte anchors: range={range}, damage={damage}, level={level}");
    }

    private static float EffectiveLevel(Skills.Skill skill)
    {
        var level = LevelSystem.Instance;
        var bonus = Math.Min(0.5f, Math.Max(0f,
            level.getAddPhysicDamage() / 40f + level.getAddAttackSpeed() / 40f));
        return skill.m_level * (1f + bonus);
    }

    private static void Damage(Character target, HitData hit)
    {
        if (ready && defender != null)
        {
            hit.m_damage = LegendsEconomyPatch.Physical(defender,
                defender.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill),
                LevelSystem.Instance.getParameter(Parameter.Special), 1.10f,
                VL_GlobalConfigs.c_duelistRiposte);
            hit.m_pushForce = pushForce;
            hit.m_dir = defender.transform.position - target.transform.position;
            hit.m_dir.y = 0f;
            hit.m_dir.Normalize();
            hit.m_skill = ValheimLegends.ValheimLegends.DisciplineSkill;
            hit.SetAttacker(defender);
        }
        target.Damage(hit);
    }
}
