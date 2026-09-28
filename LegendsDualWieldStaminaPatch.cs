using HarmonyLib;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDualWieldStaminaPatch
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Attack), "GetAttackStamina"),
        postfix: new HarmonyMethod(typeof(LegendsDualWieldStaminaPatch), nameof(Adjust)));

    private static void Adjust(Humanoid ___m_character, ref float __result)
    {
        if (___m_character is not Player player || player != Player.m_localPlayer || __result <= 0f) return;
        var left = player.LeftItem?.m_shared;
        var right = player.RightItem?.m_shared;
        if (left == null || right == null ||
            left.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon ||
            right.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon) return;
        var selected = ValheimLegends.ValheimLegends.vl_player?.vl_class;
        if (selected == ValheimLegends.ValheimLegends.PlayerClass.Berserker ||
            selected == ValheimLegends.ValheimLegends.PlayerClass.Rogue &&
            left.m_skillType == Skills.SkillType.Knives && right.m_skillType == Skills.SkillType.Knives)
            __result *= 0.5f;
    }
}
