using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMageDamagePatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Mage), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsMageDamagePatch), nameof(FrostCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Mage), "Process_Input"),
            transpiler: new HarmonyMethod(typeof(LegendsMageDamagePatch), nameof(FireCalls)));
    }

    private static IEnumerable<CodeInstruction> FrostCalls(IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, nameof(FlameNova), nameof(IceShard), 1, 1);

    private static IEnumerable<CodeInstruction> FireCalls(IEnumerable<CodeInstruction> instructions) =>
        Replace(instructions, nameof(FrostNova), nameof(FireProjectile), 1, 2);

    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions,
        string directName, string projectileName, int expectedDirect, int expectedProjectiles)
    {
        var direct = 0;
        var projectiles = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMageDamagePatch), directName);
                direct++;
            }
            else if (instruction.operand is MethodInfo setup && setup.DeclaringType == typeof(Projectile) &&
                     setup.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMageDamagePatch), projectileName);
                projectiles++;
            }
            yield return instruction;
        }
        if (direct != expectedDirect || projectiles != expectedProjectiles)
            throw new InvalidOperationException($"Unexpected Dekas Mage call sites: damage={direct}, projectile={projectiles}");
    }

    private static float School(Player caster) =>
        caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill);

    private static void FrostNova(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage.m_frost = LegendsEconomyPatch.Magic(School(caster),
            LevelSystem.Instance.getParameter(Parameter.Vigour), 0.45f, VL_GlobalConfigs.c_mageFrostNova);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void FlameNova(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage.m_fire = LegendsEconomyPatch.Magic(School(caster), 1.20f,
            VL_GlobalConfigs.c_mageInferno);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void IceShard(Projectile projectile, Character owner, Vector3 velocity,
        float noise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var amount = LegendsEconomyPatch.Magic(School(caster),
            LevelSystem.Instance.getParameter(Parameter.Agility), 0.40f, VL_GlobalConfigs.c_mageFrostDagger);
        hit.m_damage.m_pierce = amount * 0.5f;
        hit.m_damage.m_frost = amount * 0.5f;
        hit.m_toolTier = 138;
        hit.SetAttacker(caster);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void FireProjectile(Projectile projectile, Character owner, Vector3 velocity,
        float noise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var meteor = projectile.name.StartsWith("Meteor", StringComparison.Ordinal);
        var amount = meteor
            ? LegendsEconomyPatch.Magic(School(caster), 2.00f, VL_GlobalConfigs.c_mageMeteor)
            : LegendsEconomyPatch.Magic(School(caster), 0.80f, VL_GlobalConfigs.c_mageFireball);
        hit.m_damage.m_fire = amount * 0.5f;
        hit.m_damage.m_blunt = amount * 0.5f;
        hit.SetAttacker(caster);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }
}
