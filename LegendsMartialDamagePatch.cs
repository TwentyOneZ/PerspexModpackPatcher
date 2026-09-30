using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMartialDamagePatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Duelist), "Execute_Slash"),
            transpiler: new HarmonyMethod(typeof(LegendsMartialDamagePatch), nameof(DuelistSlashCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Duelist), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsMartialDamagePatch), nameof(DuelistInput)),
            transpiler: new HarmonyMethod(typeof(LegendsMartialDamagePatch), nameof(DuelistInputCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Shaman), "Process_Input"),
            transpiler: new HarmonyMethod(typeof(LegendsMartialDamagePatch), nameof(ShamanCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Berserker), "Execute_Dash"),
            transpiler: new HarmonyMethod(typeof(LegendsMartialDamagePatch), nameof(BerserkerCalls)));
    }

    private static IEnumerable<CodeInstruction> DuelistSlashCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(SeismicSlash), null, 1, 0);
    private static IEnumerable<CodeInstruction> DuelistInputCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, null, nameof(HipShot), 0, 1);
    private static IEnumerable<CodeInstruction> ShamanCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(SpiritBomb), null, 1, 0);
    private static IEnumerable<CodeInstruction> BerserkerCalls(IEnumerable<CodeInstruction> source) =>
        Replace(source, nameof(Dash), null, 1, 0);

    private static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> source, string directName,
        string projectileName, int expectedDirect, int expectedProjectiles)
    {
        var direct = 0;
        var projectiles = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMartialDamagePatch), directName);
                direct++;
            }
            else if (instruction.operand is MethodInfo setup && setup.DeclaringType == typeof(Projectile) &&
                     setup.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsMartialDamagePatch), projectileName);
                projectiles++;
            }
            yield return instruction;
        }
        if (direct != expectedDirect || projectiles != expectedProjectiles)
            throw new InvalidOperationException($"Unexpected Dekas martial call sites: damage={direct}, projectile={projectiles}");
    }

    private static float Skill(Player caster, Skills.SkillType type) => caster.GetSkills().GetSkillLevel(type);

    private static bool DuelistInput(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability3_Input_Down || !player.IsSitting()) return true;
        LegendsClassRepairPatch.Repair(player, LegendsClassRepairPatch.Kind.Duelist);
        return false;
    }

    private static void SeismicSlash(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var school = Skill(caster, ValheimLegends.ValheimLegends.DisciplineSkill);
        var factor = (0.75f + 0.005f * Mathf.Clamp(school, 0f, 100f)) * 0.90f *
                     VL_GlobalConfigs.g_DamageModifer * VL_GlobalConfigs.c_duelistSeismicSlash;
        hit.m_damage = LegendsEconomyPatch.WeaponDamage(caster);
        hit.m_damage.Modify(factor);
        hit.m_dir = caster.transform.forward;
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void HipShot(Projectile projectile, Character owner, Vector3 velocity, float noise,
        HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        hit.m_damage = LegendsEconomyPatch.Physical(caster,
            Skill(caster, ValheimLegends.ValheimLegends.DisciplineSkill),
            LevelSystem.Instance.getParameter(Parameter.Agility), 0.70f, VL_GlobalConfigs.c_duelistHipShot);
        hit.SetAttacker(owner);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void SpiritBomb(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var amount = LegendsEconomyPatch.Magic(Skill(caster, ValheimLegends.ValheimLegends.EvocationSkill),
            0.75f, VL_GlobalConfigs.c_shamanSpiritShock);
        hit.m_damage.m_spirit = amount * 0.5f;
        hit.m_damage.m_lightning = amount * 0.5f;
        hit.SetAttacker(caster);
        target.Damage(hit);
        LegendsSpiritDrainPatch.Apply(target, caster);
    }

    private static void Dash(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        hit.m_damage = LegendsEconomyPatch.Physical(caster,
            Skill(caster, ValheimLegends.ValheimLegends.DisciplineSkill), 50f, 1.8f,
            VL_GlobalConfigs.c_berserkerDash);
        var berserk = caster.GetSEMan().GetStatusEffect("SE_VL_Berserk".GetStableHashCode()) as SE_Berserk;
        if (berserk != null) hit.ApplyModifier(berserk.damageModifier);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }
}
