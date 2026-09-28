using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsSpiritDrainPatch
{
    private sealed class DrainState
    {
        internal ZDOID Attacker;
        internal float Damage;
    }

    private static readonly ConditionalWeakTable<SE_SpiritDrain, DrainState> States = new();
    [ThreadStatic] private static SE_SpiritDrain active;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Shaman), "Process_Input"),
            transpiler: new HarmonyMethod(typeof(LegendsSpiritDrainPatch), nameof(ShamanCalls)));
        harmony.Patch(AccessTools.Method(typeof(Character), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsSpiritDrainPatch), nameof(CharacterAwake)));
        harmony.Patch(AccessTools.Method(typeof(SE_SpiritDrain), nameof(SE_SpiritDrain.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsSpiritDrainPatch), nameof(TickStart)),
            postfix: new HarmonyMethod(typeof(LegendsSpiritDrainPatch), nameof(TickEnd)),
            transpiler: new HarmonyMethod(typeof(LegendsSpiritDrainPatch), nameof(TickCalls)));
    }

    private static void CharacterAwake(Character __instance)
    {
        var view = __instance.GetComponent<ZNetView>();
        if (view == null) return;
        view.Register<ZDOID, float>("Perspex_SpiritDrain", (sender, attacker, damage) =>
        {
            if (!view.IsValid() || !view.IsOwner()) return;
            var effects = __instance.GetSEMan();
            var hash = "SE_VL_SpiritDrain".GetStableHashCode();
            effects.AddStatusEffect(hash, true, 0, 0f);
            var drain = effects.GetStatusEffect(hash) as SE_SpiritDrain;
            if (drain == null) return;
            States.GetOrCreateValue(drain).Attacker = attacker;
            States.GetOrCreateValue(drain).Damage = damage;
        });
    }

    internal static void Apply(Character target, Player caster)
    {
        var view = target.GetComponent<ZNetView>();
        if (view == null || !view.IsValid()) return;
        var amount = LegendsEconomyPatch.Magic(
            caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill),
            EpicMMOSystem.LevelSystem.Instance.getParameter(EpicMMOSystem.Parameter.Body),
            0.10f, VL_GlobalConfigs.c_shamanSpiritShock);
        view.InvokeRPC("Perspex_SpiritDrain", caster.GetZDOID(), amount);
    }

    private static void TickStart(SE_SpiritDrain __instance) => active = __instance;
    private static void TickEnd() => active = null;

    private static IEnumerable<CodeInstruction> ShamanCalls(IEnumerable<CodeInstruction> source)
    {
        var statusCalls = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(SEMan) &&
                method.Name == nameof(SEMan.AddStatusEffect) && method.GetParameters()[0].ParameterType == typeof(StatusEffect))
            {
                statusCalls++;
                if (statusCalls == 2)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegendsSpiritDrainPatch), nameof(SkipOriginalDrain));
                }
            }
            yield return instruction;
        }
        if (statusCalls < 2) throw new InvalidOperationException($"Unexpected Dekas Shaman status calls: {statusCalls}");
    }

    private static StatusEffect SkipOriginalDrain(SEMan effects, StatusEffect drain, bool reset,
        int level, float skillLevel) => null;

    private static IEnumerable<CodeInstruction> TickCalls(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.ApplyDamage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsSpiritDrainPatch), nameof(ApplyDamage));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Dekas Spirit Drain damage calls: {count}");
    }

    private static void ApplyDamage(Character target, HitData hit, bool showText, bool effects,
        HitData.DamageModifier modifier)
    {
        if (active != null && States.TryGetValue(active, out var state))
        {
            hit.m_damage.m_spirit = state.Damage;
            hit.m_attacker = state.Attacker;
            hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
        }
        target.ApplyDamage(hit, showText, effects, modifier);
    }
}
