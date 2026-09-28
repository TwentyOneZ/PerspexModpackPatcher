using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsCompanionPatch
{
    private sealed class State { internal float Check; internal float Scale = -1f; }
    private static readonly ConditionalWeakTable<SE_Companion, State> States = new();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SE_Companion), nameof(SE_Companion.UpdateStatusEffect)),
            postfix: new HarmonyMethod(typeof(LegendsCompanionPatch), nameof(Update)),
            transpiler: new HarmonyMethod(typeof(LegendsCompanionPatch), nameof(CompanionTick)));
        harmony.Patch(AccessTools.Method(typeof(SE_Companion), nameof(SE_Companion.ModifySpeed)),
            prefix: new HarmonyMethod(typeof(LegendsCompanionPatch), nameof(Speed)));
        harmony.Patch(AccessTools.Method(typeof(SE_Companion), nameof(SE_Companion.OnDamaged)),
            prefix: new HarmonyMethod(typeof(LegendsCompanionPatch), nameof(Damage)));
        harmony.Patch(AccessTools.Method(typeof(SE_Companion), nameof(SE_Companion.IsDone)),
            transpiler: new HarmonyMethod(typeof(LegendsCompanionPatch), nameof(CompanionExpiry)));
    }

    private static bool Speed(SE_Companion __instance, ref float speed)
    {
        speed *= __instance.speedModifier;
        return false;
    }

    private static bool Damage() => false;

    private static IEnumerable<CodeInstruction> CompanionTick(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(VL_Utility) &&
                method.Name == "UpdateCompanionTarget")
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsCompanionPatch), nameof(NoTarget));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Unexpected companion target call sites");
    }

    private static void NoTarget(SE_Companion effect) { }

    private static IEnumerable<CodeInstruction> CompanionExpiry(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.ApplyDamage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsCompanionPatch), nameof(Expire));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException("Unexpected companion expiry call sites");
    }

    private static void Expire(Character character, HitData hit, bool text, bool effects,
        HitData.DamageModifier modifier) => character.ApplyDamage(hit, false, false, modifier);

    private static void Update(SE_Companion __instance, float dt)
    {
        var character = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (character == null) return;
        var view = character.GetComponent<ZNetView>();
        if (view == null || !view.IsValid()) return;
        var zdo = view.GetZDO();
        if (zdo == null) return;

        var state = States.GetOrCreateValue(__instance);
        var scale = zdo.GetFloat("VL_Companion_Scale", 1f);
        if (scale > 0f && Math.Abs(state.Scale - scale) >= 0.001f)
        {
            character.transform.localScale = Vector3.one * scale;
            state.Scale = scale;
        }

        state.Check -= dt;
        if (state.Check > 0f || !view.IsOwner()) return;
        state.Check = 0.5f;
        var owner = __instance.summoner;
        if (owner == null || owner.IsDead())
        {
            var ownerId = zdo.GetZDOID("VL_Companion_Summoner");
            if (ownerId != ZDOID.None)
                owner = ZNetScene.instance?.FindInstance(ownerId)?.GetComponent<Player>();
            if (owner != null) __instance.summoner = owner;
        }
        if (owner != null && !owner.IsDead() &&
            Vector3.Distance(character.transform.position, owner.transform.position) > 60f)
            character.transform.SetPositionAndRotation(owner.transform.position + Vector3.up * 2f,
                Quaternion.identity);
    }
}
