using System;
using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDualWieldStaminaPatch
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Attack), "GetAttackStamina"),
        postfix: new HarmonyMethod(typeof(LegendsDualWieldStaminaPatch), nameof(Adjust))
            { priority = Priority.Last });

    // DualWield supplies a separate stamina cost; m_attackStamina can be much higher.
    // Restore the class discount while capping the result at a single weapon's cost.
    private static void Adjust(Attack __instance, Humanoid ___m_character,
        ItemDrop.ItemData ___m_weapon, ref float __result)
    {
        if (___m_character is not Player player || player != Player.m_localPlayer ||
            ___m_weapon == null || __result <= 0f ||
            player.LeftItem?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon ||
            player.RightItem?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon)
            return;

        var selected = ValheimLegends.ValheimLegends.vl_player?.vl_class;
        var berserker = selected == ValheimLegends.ValheimLegends.PlayerClass.Berserker;
        if (!berserker &&
            (selected != ValheimLegends.ValheimLegends.PlayerClass.Rogue ||
             player.LeftItem.m_shared.m_skillType != Skills.SkillType.Knives ||
             player.RightItem.m_shared.m_skillType != Skills.SkillType.Knives))
            return;

        if (__instance.m_attackStamina <= 0f)
        {
            __result = 0f;
            return;
        }

        var cost = __instance.m_attackStamina * (1f + (__instance.m_isHomeItem
            ? player.GetEquipmentHomeItemModifier()
            : player.GetEquipmentAttackStaminaModifier()));
        player.GetSEMan().ModifyAttackStaminaUsage(cost, ref cost);
        cost -= cost * 0.33f * player.GetSkillFactor(___m_weapon.m_shared.m_skillType);
        if (__instance.m_staminaReturnPerMissingHP > 0f)
            cost -= (player.GetMaxHealth() - player.GetHealth()) * __instance.m_staminaReturnPerMissingHP;
        __result = Math.Min(__result * 0.3f * (berserker ? VL_GlobalConfigs.c_berserkerBonus2h : 1f),
            Math.Max(0f, cost));
    }
}
