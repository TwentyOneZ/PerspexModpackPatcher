using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMonkDamagePatch
{
    private static readonly int MonkHash = "SE_VL_Monk".GetStableHashCode();
    private static readonly FieldInfo MaxHitCount = AccessTools.Field(typeof(SE_Monk), "maxHitCount");

    internal static void Install(Harmony harmony)
    {
        var legends = typeof(ValheimLegends.ValheimLegends);
        var damagePatch = AccessTools.Method(legends.GetNestedType("VL_Damage_Patch",
            BindingFlags.Public | BindingFlags.NonPublic), "Prefix");
        var blockPatch = AccessTools.Method(legends.GetNestedType("BaseBlockPower_Bulwark_Patch",
            BindingFlags.Public | BindingFlags.NonPublic), "Postfix");
        if (damagePatch == null || blockPatch == null || MaxHitCount == null)
            throw new MissingMethodException("Dekas 0.7.10 Monk damage or block patch missing");
        harmony.Patch(damagePatch,
            transpiler: new HarmonyMethod(typeof(LegendsMonkDamagePatch), nameof(NeutralizeBonus)),
            postfix: new HarmonyMethod(typeof(LegendsMonkDamagePatch), nameof(AfterHit)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsMonkDamagePatch), nameof(BeforeDamage)) { priority = Priority.First });
        harmony.Patch(blockPatch,
            postfix: new HarmonyMethod(typeof(LegendsMonkDamagePatch), nameof(AfterBlock)));
    }

    private static IEnumerable<CodeInstruction> NeutralizeBonus(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && value == 1.25f)
            {
                count++;
                instruction.operand = 1f;
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException($"Unexpected Dekas Monk multiplier anchors: {count}");
    }

    private static bool Eligible(HitData hit, out Player player)
    {
        player = hit?.GetAttacker() as Player;
        return player != null && player == Player.m_localPlayer &&
               player.GetSEMan().HaveStatusEffect(MonkHash) && Class_Monk.PlayerIsUnarmed &&
               (hit.m_damage.m_blunt > 0f || hit.m_damage.m_slash > 0f);
    }

    private static void BeforeDamage(HitData hit)
    {
        RoguePoison(hit);
        if (!Eligible(hit, out var player)) return;
        var level = LevelSystem.Instance;
        var unarmed = player.GetSkills().GetSkillLevel(Skills.SkillType.Unarmed);
        var discipline = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var baseDamage = (4f + 0.9f * level.getLevel()) *
            (0.75f + 0.005f * Mathf.Clamp(unarmed, 0f, 100f)) *
            (0.75f + 0.005f * Mathf.Clamp(discipline, 0f, 100f)) *
            (1f + level.getAddPhysicDamage() / 100f);
        var weapon = player.GetCurrentWeapon();
        if (weapon?.m_shared?.m_name == "Unarmed" && !player.GetInventory().ContainsItem(weapon))
            hit.m_damage = new HitData.DamageTypes { m_blunt = baseDamage * 0.6f, m_spirit = baseDamage * 0.4f };
        else hit.m_damage.m_spirit += baseDamage * 0.4f;
    }

    private static void RoguePoison(HitData hit)
    {
        if (hit?.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            !player.GetSEMan().HaveStatusEffect("SE_VL_Rogue".GetStableHashCode())) return;
        var right = player.GetCurrentWeapon()?.m_shared;
        var left = Traverse.Create(player).Field("m_leftItem").GetValue<ItemDrop.ItemData>()?.m_shared;
        if (right == null ||
            (left == null
                ? right.m_skillType != Skills.SkillType.Knives && !right.m_name.ToLowerInvariant().Contains("skoll")
                : (right.m_skillType != Skills.SkillType.Knives || left.m_skillType != Skills.SkillType.Knives) &&
                  !right.m_name.ToLowerInvariant().Contains("skoll") &&
                  !left.m_name.ToLowerInvariant().Contains("skoll"))) return;
        var level = LevelSystem.Instance;
        var alteration = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill) *
            (1f + Mathf.Clamp(level.getAddCriticalChance() / 40f + level.getAddMagicDamage() / 80f, 0f, 0.5f));
        hit.m_damage.m_poison += 0.5f * level.getLevel() * (1f + alteration / 80f);
        player.RaiseSkill(ValheimLegends.ValheimLegends.AlterationSkill,
            0.001f * VL_GlobalConfigs.g_SkillGainModifer * (1f + level.getAddMagicDamage() / 16f));
    }

    private static void AfterHit(ref HitData __1)
    {
        if (!Eligible(__1, out var player)) return;
        if (player.GetSEMan().GetStatusEffect(MonkHash) is not SE_Monk monk) return;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var cap = 5 + Mathf.RoundToInt(0.4f * Mathf.Sqrt(skill));
        MaxHitCount.SetValue(monk, cap);
        monk.hitCount = Mathf.Clamp(monk.hitCount, 0, cap);
        player.RaiseSkill(ValheimLegends.ValheimLegends.DisciplineSkill,
            0.001f * VL_GlobalConfigs.g_SkillGainModifer *
            (1f + LevelSystem.Instance.getAddMagicDamage() / 16f));
    }

    private static void AfterBlock(ItemDrop.ItemData __0, ref float __1)
    {
        var player = Player.m_localPlayer;
        if (player == null || __0?.m_shared?.m_name != "Unarmed" ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Monk) return;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var level = LevelSystem.Instance;
        var multiplier = 1f + Mathf.Clamp(level.getAddPhysicDamage() / 40f +
                                          level.getAddAttackSpeed() / 40f, 0f, 0.5f);
        __1 += skill * VL_GlobalConfigs.c_monkBonusBlock * (0.5f * multiplier - 1f);
    }
}
