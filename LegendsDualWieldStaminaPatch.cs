using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDualWieldStaminaPatch
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Attack), "GetAttackStamina"),
        prefix: new HarmonyMethod(typeof(LegendsDualWieldStaminaPatch), nameof(SingleWeaponCost))
            { priority = Priority.First });

    // DualWield replaces this method with its own configured costs. For these classes,
    // use Valheim's single-weapon formula with the attack's actual weapon and stamina cost.
    private static bool SingleWeaponCost(Attack __instance, Humanoid ___m_character,
        ItemDrop.ItemData ___m_weapon, ref float __result)
    {
        if (___m_character is not Player player || player != Player.m_localPlayer ||
            ___m_weapon == null ||
            player.LeftItem?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon ||
            player.RightItem?.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon)
            return true;

        var selected = ValheimLegends.ValheimLegends.vl_player?.vl_class;
        if (selected != ValheimLegends.ValheimLegends.PlayerClass.Berserker &&
            (selected != ValheimLegends.ValheimLegends.PlayerClass.Rogue ||
             player.LeftItem.m_shared.m_skillType != Skills.SkillType.Knives ||
             player.RightItem.m_shared.m_skillType != Skills.SkillType.Knives))
            return true;

        if (__instance.m_attackStamina <= 0f)
        {
            __result = 0f;
            return false;
        }

        var cost = __instance.m_attackStamina * (1f + (__instance.m_isHomeItem
            ? player.GetEquipmentHomeItemModifier()
            : player.GetEquipmentAttackStaminaModifier()));
        player.GetSEMan().ModifyAttackStaminaUsage(cost, ref cost);
        cost -= cost * 0.33f * player.GetSkillFactor(___m_weapon.m_shared.m_skillType);
        if (__instance.m_staminaReturnPerMissingHP > 0f)
            cost -= (player.GetMaxHealth() - player.GetHealth()) * __instance.m_staminaReturnPerMissingHP;
        __result = cost;
        return false;
    }
}
