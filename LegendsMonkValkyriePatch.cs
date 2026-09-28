using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMonkValkyriePatch
{
    [ThreadStatic] private static float monkAltitude;
    [ThreadStatic] private static float valkyrieAltitude;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Monk), "Impact_Effect"),
            prefix: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(MonkImpactStart)),
            transpiler: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(MonkImpactCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Monk), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(MonkAttackCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Valkyrie), "Impact_Effect"),
            prefix: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(ValkyrieImpactStart)),
            transpiler: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(ValkyrieImpactCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Valkyrie), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsMonkValkyriePatch), nameof(ValkyrieAttackCalls)));
    }

    private static void MonkImpactStart(float altitude) => monkAltitude = altitude;
    private static void ValkyrieImpactStart(float altitude) => valkyrieAltitude = altitude;
    private static IEnumerable<CodeInstruction> MonkImpactCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(ChiSlam), null, 1, 0);
    private static IEnumerable<CodeInstruction> MonkAttackCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(MonkAttack), nameof(PsiBolt), 3, 1);
    private static IEnumerable<CodeInstruction> ValkyrieImpactCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(Leap), null, 1, 0);
    private static IEnumerable<CodeInstruction> ValkyrieAttackCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(ChillWave), nameof(IceLance), 1, 1);

    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> source, string damageName,
        string setupName, int expectedDamage, int expectedSetup)
    {
        var damage = 0;
        var setup = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMonkValkyriePatch), damageName);
                damage++;
            }
            else if (instruction.operand is MethodInfo projectile && projectile.DeclaringType == typeof(Projectile) &&
                     projectile.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMonkValkyriePatch), setupName);
                setup++;
            }
            yield return instruction;
        }
        if (damage != expectedDamage || setup != expectedSetup)
            throw new InvalidOperationException($"Unexpected Dekas Monk/Valkyrie calls: damage={damage}, projectile={setup}");
    }

    private static float Skill(Player caster, Skills.SkillType type) => caster.GetSkills().GetSkillLevel(type);
    private static float Attribute(Parameter type) => LevelSystem.Instance.getParameter(type);
    private static HitData.DamageTypes Physical(Player caster, float secondary, float coefficient, float config) =>
        LegendsEconomyPatch.Physical(caster, Skill(caster, ValheimLegends.ValheimLegends.DisciplineSkill),
            secondary, coefficient, config);

    private static void ChiSlam(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage = Physical(caster, Attribute(Parameter.Vigour), 2.5f, VL_GlobalConfigs.c_monkChiSlam);
        hit.m_damage.Modify(1f + Mathf.Clamp01(3f * monkAltitude / Mathf.Max(1f, hit.m_damage.GetTotalDamage())));
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void MonkAttack(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        if (hit.m_pushForce >= 40f)
            hit.m_damage = Physical(caster, Attribute(Parameter.Special), 1.5f, VL_GlobalConfigs.c_monkChiPunch);
        else
            hit.m_damage = Physical(caster, Attribute(Parameter.Agility), hit.m_pushForce >= 9f ? 0.55f : 0.22f,
                VL_GlobalConfigs.c_monkFlyingKick);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void PsiBolt(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var monk = caster.GetSEMan().GetStatusEffect("SE_VL_Monk".GetStableHashCode()) as SE_Monk;
        var count = monk?.hitCount ?? 0;
        hit.m_damage = Physical(caster, Attribute(Parameter.Special), 0.30f * count, VL_GlobalConfigs.c_monkChiBlast);
        hit.SetAttacker(owner);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void Leap(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage = Physical(caster, Attribute(Parameter.Vigour), 0.75f, VL_GlobalConfigs.c_valkyrieLeap);
        hit.m_damage.Modify(1f + Mathf.Clamp01(3f * valkyrieAltitude / Mathf.Max(1f, hit.m_damage.GetTotalDamage())));
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void ChillWave(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var status = caster.GetSEMan().GetStatusEffect("SE_VL_Valkyrie".GetStableHashCode()) as SE_Valkyrie;
        var amount = LegendsEconomyPatch.Magic(Skill(caster, ValheimLegends.ValheimLegends.AbjurationSkill),
            Attribute(Parameter.Vigour), 0.22f * (status?.hitCount ?? 0), VL_GlobalConfigs.c_valkyrieBonusChillWave);
        hit.m_damage.m_frost = amount * 0.5f;
        hit.m_damage.m_spirit = amount * 0.5f;
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void IceLance(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        hit.m_damage = Physical((Player)owner, Attribute(Parameter.Agility), 0.75f,
            VL_GlobalConfigs.c_valkyrieBonusIceLance);
        hit.SetAttacker(owner);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }
}
