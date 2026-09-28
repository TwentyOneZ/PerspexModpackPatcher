using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsPriestPatch
{
    private static readonly FieldInfo HealCount = AccessTools.Field(typeof(Class_Priest), "healCount");

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Priest), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsPriestPatch), nameof(Vial)),
            transpiler: new HarmonyMethod(typeof(LegendsPriestPatch), nameof(DamageCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Priest), "HealNearbyPlayers"),
            prefix: new HarmonyMethod(typeof(LegendsPriestPatch), nameof(CorrectHealing)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(VL_Utility), "GetHealCostPerUpdate"),
            prefix: new HarmonyMethod(typeof(LegendsPriestPatch), nameof(PerFrameCost)));
    }

    private static bool PerFrameCost(ref float __result)
    {
        __result = VL_Utility.GetHealCost * Time.deltaTime;
        return false;
    }

    private static void CorrectHealing(Player healer, ref float amount)
    {
        if (healer != Player.m_localPlayer) return;
        var skill = healer.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        if (VL_Utility.Ability3_Input_Down)
            amount = LegendsEconomyPatch.Healing(15f, skill) * VL_GlobalConfigs.c_priestHeal;
        else if (VL_Utility.Ability3_Input_Pressed)
        {
            var pulses = (int)HealCount.GetValue(null);
            var ratio = (pulses + skill * 0.3f) * 2f / (10f + skill);
            amount = LegendsEconomyPatch.Healing(15f, skill) * ratio * VL_GlobalConfigs.c_priestHeal;
        }
        else if (VL_Utility.Ability2_Input_Down)
            amount = LegendsEconomyPatch.Healing(5f, skill) * VL_GlobalConfigs.c_priestPurgeHeal;
    }

    private static bool Vial(Player player)
    {
        if (player != Player.m_localPlayer || !player.IsBlocking() || !VL_Utility.Ability3_Input_Down)
            return true;
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect("SE_VL_Ability3_CD".GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return false;
        }
        var cost = VL_Utility.GetHealCost;
        if (player.GetStamina() < cost)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {cost:0.#} stamina.");
            return false;
        }
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        var boosted = skill * (1f + Mathf.Clamp(LevelSystem.Instance.getAddCriticalChance() / 40f +
                                                 LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));
        LegendsClassCraftPatch.Craft(player, "BoneFragments", 1, "questitem_wraiths_breath", 1,
            cost, 3, VL_Utility.GetHealCooldownTime * 20f / (1f + boosted / 150f),
            ValheimLegends.ValheimLegends.AlterationSkill, VL_Utility.GetHealSkillGain);
        return false;
    }

    private static IEnumerable<CodeInstruction> DamageCalls(IEnumerable<CodeInstruction> instructions)
    {
        var direct = 0;
        var projectile = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsPriestPatch), nameof(PurgeDamage));
                direct++;
            }
            else if (instruction.operand is MethodInfo setup && setup.DeclaringType == typeof(Projectile) &&
                     setup.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsPriestPatch), nameof(SetupSanctify));
                projectile++;
            }
            yield return instruction;
        }
        if (direct != 1 || projectile != 1)
            throw new InvalidOperationException($"Unexpected Dekas Priest call sites: damage={direct}, projectile={projectile}");
    }

    private static void PurgeDamage(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill);
        var damage = LegendsEconomyPatch.Magic(school, LevelSystem.Instance.getParameter(Parameter.Body),
                                               0.60f, VL_GlobalConfigs.c_priestPurgeDamage);
        hit.m_damage.m_spirit = damage * 0.5f;
        hit.m_damage.m_fire = damage * 0.5f;
        hit.SetAttacker(caster);
        target.Damage(hit);
    }

    private static void SetupSanctify(Projectile projectile, Character owner, Vector3 velocity,
        float hitNoise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill);
        var damage = LegendsEconomyPatch.Magic(school, LevelSystem.Instance.getParameter(Parameter.Body),
                                               1.10f, VL_GlobalConfigs.c_priestSanctify);
        hit.m_damage.m_fire = damage * 0.5f;
        hit.m_damage.m_blunt = damage * 0.5f;
        hit.m_damage.m_spirit = damage;
        hit.SetAttacker(caster);
        projectile.Setup(owner, velocity, hitNoise, hit, item, ammo);
    }
}
