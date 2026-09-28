using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsClassRepairPatch
{
    internal enum Kind { Duelist, Ranger, Berserker, Valkyrie, Enchanter }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Berserker), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsClassRepairPatch), nameof(BerserkerInput)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Class_Valkyrie), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsClassRepairPatch), nameof(ValkyrieInput)) { priority = Priority.First });
    }

    private static bool BerserkerInput(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability3_Input_Down || !player.IsSitting()) return true;
        Repair(player, Kind.Berserker);
        return false;
    }

    private static bool ValkyrieInput(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability3_Input_Down || !player.IsSitting()) return true;
        Repair(player, Kind.Valkyrie);
        return false;
    }

    internal static void Repair(Player player, Kind kind)
    {
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect("SE_VL_Ability3_CD".GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return;
        }
        var weapon = kind == Kind.Valkyrie ? player.LeftItem : player.GetCurrentWeapon();
        if (kind == Kind.Enchanter && !Magic(weapon)) weapon = player.LeftItem;
        if (!Eligible(player, weapon, kind))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Equip a suitable item to repair it.");
            return;
        }
        var missing = weapon.GetMaxDurability() - weapon.m_durability;
        if (missing <= 0f)
        {
            player.Message(MessageHud.MessageType.TopLeft, "This item does not need repair.");
            return;
        }
        var level = LevelSystem.Instance;
        var school = kind == Kind.Enchanter ? ValheimLegends.ValheimLegends.AlterationSkill :
            ValheimLegends.ValheimLegends.DisciplineSkill;
        var skill = player.GetSkills().GetSkillLevel(school);
        var bonus = kind == Kind.Enchanter ? level.getAddCriticalChance() / 40f +
            level.getAddMagicDamage() / 80f : level.getAddPhysicDamage() / 40f +
            level.getAddAttackSpeed() / 40f;
        var effective = skill * (1f + Mathf.Clamp(bonus, 0f, 0.5f));
        var critical = Mathf.Clamp(1f - level.getAddCriticalChance() / 40f, 0.01f, 1f);
        var reduction = Mathf.Clamp(1f - level.getStaminaReduction() / 100f, 0.01f, 1f);
        var staminaPerDurability = 10f * critical * Mathf.Clamp(1f - effective / 300f, 0.01f, 1f) * reduction;
        var restored = Mathf.Min(missing, player.GetStamina() / staminaPerDurability);
        if (restored <= 0f)
        {
            player.Message(MessageHud.MessageType.TopLeft, "Need stamina to repair this item.");
            return;
        }
        player.UseStamina(restored * staminaPerDurability);
        weapon.m_durability = Mathf.Min(weapon.GetMaxDurability(), weapon.m_durability + restored);
        var cooldown = ScriptableObject.CreateInstance<SE_Ability3_CD>();
        cooldown.m_ttl = 60f + restored * 2f * critical;
        effects.AddStatusEffect(cooldown);
        player.RaiseSkill(school, VL_Utility.GetBlinkStrikeSkillGain);
        MageVisuals.Spawn(kind == Kind.Enchanter ? "fx_VL_Shock" :
            kind == Kind.Valkyrie ? "fx_guardstone_activate" : "fx_VL_BlinkStrike", player.GetCenterPoint());
        player.Message(MessageHud.MessageType.TopLeft, $"Repaired {weapon.m_shared.m_name}: +{restored:0.#} durability.");
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
    }

    private static bool Magic(ItemDrop.ItemData item) => item?.m_shared != null &&
        (item.m_shared.m_skillType == Skills.SkillType.ElementalMagic ||
         item.m_shared.m_skillType == Skills.SkillType.BloodMagic);

    private static bool Eligible(Player player, ItemDrop.ItemData item, Kind kind)
    {
        if (item?.m_shared == null || item.GetMaxDurability() <= 0f) return false;
        var shared = item.m_shared;
        return kind switch
        {
            Kind.Duelist => player.LeftItem == null &&
                shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
                (shared.m_skillType == Skills.SkillType.Swords || shared.m_skillType == Skills.SkillType.Knives ||
                 shared.m_skillType == Skills.SkillType.Axes || shared.m_skillType == Skills.SkillType.Spears),
            Kind.Ranger => shared.m_skillType == Skills.SkillType.Bows ||
                shared.m_skillType == Skills.SkillType.Crossbows,
            Kind.Berserker => shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft,
            Kind.Valkyrie => shared.m_itemType == ItemDrop.ItemData.ItemType.Shield,
            Kind.Enchanter => Magic(item),
            _ => false
        };
    }
}
