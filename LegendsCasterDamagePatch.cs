using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsCasterDamagePatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsCasterDamagePatch), nameof(EnchanterCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Metavoker), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsCasterDamagePatch), nameof(MetavokerAttackCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Metavoker), "Process_Input"),
            transpiler: new HarmonyMethod(typeof(LegendsCasterDamagePatch), nameof(MetavokerInputCalls)));
    }

    private static IEnumerable<CodeInstruction> EnchanterCalls(IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, nameof(BiomeShock), nameof(Charm), 1, 1);

    private static IEnumerable<CodeInstruction> MetavokerAttackCalls(IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, nameof(ForceWave), nameof(Reflect), 1, 1);

    private static IEnumerable<CodeInstruction> MetavokerInputCalls(IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, nameof(Warp), nameof(Light), 1, 2);

    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,
        string directName, string setupName, int expectedDirect, int expectedSetup)
    {
        var direct = 0;
        var setup = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsCasterDamagePatch), directName);
                direct++;
            }
            else if (instruction.operand is MethodInfo projectile && projectile.DeclaringType == typeof(Projectile) &&
                     projectile.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsCasterDamagePatch), setupName);
                setup++;
            }
            yield return instruction;
        }
        if (direct != expectedDirect || setup != expectedSetup)
            throw new InvalidOperationException($"Unexpected Dekas caster call sites: damage={direct}, projectile={setup}");
    }

    private static float Skill(Player caster, Skills.SkillType type) => caster.GetSkills().GetSkillLevel(type);

    private static void BiomeShock(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var zone = Class_Enchanter.HasZoneBuffTime(caster);
        var factor = zone == null ? 1f : 1f + Mathf.Clamp01(zone.GetRemaningTime() / Mathf.Max(1f, zone.m_ttl));
        hit.m_damage.m_lightning = LegendsEconomyPatch.Magic(Skill(caster, ValheimLegends.ValheimLegends.AlterationSkill),
            LevelSystem.Instance.getParameter(Parameter.Special), 0.80f, VL_GlobalConfigs.c_enchanterBiomeShock) * factor;
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void ForceWave(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage.m_damage = LegendsEconomyPatch.Magic(Skill(caster, ValheimLegends.ValheimLegends.EvocationSkill),
            LevelSystem.Instance.getParameter(Parameter.Vigour), 0.35f, VL_GlobalConfigs.c_metavokerBonusForceWave);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void Warp(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        // Dekas 0.7.10 encodes distance as (distance + raw skill) / 10 in push force.
        var level = Skill(caster, ValheimLegends.ValheimLegends.EvocationSkill);
        var distance = Mathf.Max(0f, hit.m_pushForce * 10f - level);
        hit.m_damage.m_lightning = LegendsEconomyPatch.Magic(Skill(caster, ValheimLegends.ValheimLegends.EvocationSkill),
            LevelSystem.Instance.getParameter(Parameter.Agility), 0.55f, VL_GlobalConfigs.c_metavokerWarpDamage) *
            (1f + Mathf.Clamp01(distance / 140f));
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void Charm(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        hit.SetAttacker(owner);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void Reflect(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo) =>
        projectile.Setup(owner, velocity, noise, hit, item, ammo);

    private static void Light(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        if (hit.m_damage.GetTotalDamage() > 0f)
        {
            var amount = LegendsEconomyPatch.Magic(Skill((Player)owner, ValheimLegends.ValheimLegends.IllusionSkill),
                0.65f, VL_GlobalConfigs.c_meteavokerLight);
            hit.m_damage.m_lightning = amount * 0.5f;
            hit.m_damage.m_pierce = amount * 0.5f;
        }
        hit.SetAttacker(owner);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }
}
