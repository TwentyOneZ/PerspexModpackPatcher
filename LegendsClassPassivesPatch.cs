using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsClassPassivesPatch
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
        prefix: new HarmonyMethod(typeof(LegendsClassPassivesPatch), nameof(BeforeDamage))
        { priority = Priority.Last });

    private static void BeforeDamage(HitData hit)
    {
        if (hit?.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_name != player.GetPlayerName()) return;
        var weapon = player.GetCurrentWeapon();
        var mmo = LevelSystem.Instance;
        var chance = (5f + mmo.getAddCriticalChance()) / 100f;
        switch (ValheimLegends.ValheimLegends.vl_player.vl_class)
        {
            case ValheimLegends.ValheimLegends.PlayerClass.Berserker:
                var health = Mathf.Clamp01(player.GetHealthPercentage());
                var config = VL_GlobalConfigs.c_berserkerBonusDamage;
                var original = 1f + (1f - health) * 0.4f * config;
                var local = Mathf.Clamp(1f + (1f - Mathf.Sqrt(health)) * config, 1f, 2f);
                if (original > 0f) hit.ApplyModifier(local / original);
                break;
            case ValheimLegends.ValheimLegends.PlayerClass.Priest:
                if (weapon?.m_shared?.m_skillType != Skills.SkillType.Clubs || hit.m_skill != Skills.SkillType.Clubs)
                    break;
                var discipline = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill) *
                    (1f + Mathf.Clamp(mmo.getAddPhysicDamage() / 40f +
                                      mmo.getAddAttackSpeed() / 40f, 0f, 0.5f));
                hit.m_damage.m_spirit += mmo.getLevel() * (1f + discipline / 80f) * 0.5f;
                player.RaiseSkill(ValheimLegends.ValheimLegends.DisciplineSkill,
                    0.001f * VL_GlobalConfigs.g_SkillGainModifer * (1f + mmo.getAddMagicDamage() / 16f));
                break;
            case ValheimLegends.ValheimLegends.PlayerClass.Ranger:
                if (weapon == null || weapon.m_shared.m_skillType != hit.m_skill ||
                    weapon.m_shared.m_skillType != Skills.SkillType.Bows &&
                    !(weapon.m_shared.m_skillType == Skills.SkillType.Spears && hit.m_ranged) ||
                    UnityEngine.Random.value >= chance) break;
                hit.ApplyModifier(hit.m_backstabBonus);
                player.Message(MessageHud.MessageType.TopLeft,
                    $"Ranged critical! ({hit.m_backstabBonus:F1}x damage)");
                break;
            case ValheimLegends.ValheimLegends.PlayerClass.Duelist:
                if (weapon == null || hit.m_ranged ||
                    Traverse.Create(player).Field("m_leftItem").GetValue<ItemDrop.ItemData>() != null ||
                    weapon.m_shared.m_skillType != hit.m_skill ||
                    weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                    weapon.m_shared.m_skillType != Skills.SkillType.Swords &&
                    weapon.m_shared.m_skillType != Skills.SkillType.Knives &&
                    weapon.m_shared.m_skillType != Skills.SkillType.Axes &&
                    weapon.m_shared.m_skillType != Skills.SkillType.Spears ||
                    UnityEngine.Random.value >= chance) break;
                hit.ApplyModifier(hit.m_backstabBonus * 0.5f);
                player.Message(MessageHud.MessageType.TopLeft,
                    $"Melee critical! ({hit.m_backstabBonus * 0.5f:F1}x damage)");
                break;
        }
    }
}
