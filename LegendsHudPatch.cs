using System;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsHudPatch
{
    internal static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Hud), "UpdateStatusEffects");
        if (target == null) return;
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(LegendsHudPatch), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(LegendsHudPatch), nameof(Postfix)));
    }

    private static void Prefix(Hud __instance)
    {
        var icons = ValheimLegends.ValheimLegends.abilitiesStatus;
        if (icons == null || icons.Count == 0) return;
        var ready = Player.m_localPlayer != null && ValheimLegends.ValheimLegends.ClassIsValid &&
                    ValheimLegends.ValheimLegends.showAbilityIcons.Value && icons.Count == 3;
        if (ready)
            foreach (var icon in icons)
                if (icon == null || icon.parent != __instance.m_statusEffectListRoot) ready = false;
        if (ready) return;
        foreach (var icon in icons)
            if (icon != null) UnityEngine.Object.Destroy(icon.gameObject);
        icons.Clear();
    }

    [HarmonyAfter("valheim.torann.valheimlegends")]
    private static void Postfix(Hud __instance)
    {
        var legends = ValheimLegends.ValheimLegends.abilitiesStatus;
        if (Player.m_localPlayer == null || !ValheimLegends.ValheimLegends.ClassIsValid ||
            !ValheimLegends.ValheimLegends.showAbilityIcons.Value || legends == null ||
            legends.Count != 3 || __instance.m_gpRoot == null) return;

        var root = __instance.m_statusEffectListRoot;
        var center = RectTransformUtility.CalculateRelativeRectTransformBounds(root, __instance.m_gpRoot).center;
        var ownCenter = root.rect.center;
        var vertical = ValheimLegends.ValheimLegends.iconAlignment.Value.Equals("vertical", StringComparison.OrdinalIgnoreCase);
        for (var index = 0; index < legends.Count; index++)
        {
            var icon = legends[index];
            if (icon == null) continue;
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = new Vector2(
                center.x - ownCenter.x + 209f + ValheimLegends.ValheimLegends.icon_X_Offset.Value + (vertical ? 0 : 80 * index),
                center.y - ownCenter.y + 106f + ValheimLegends.ValheimLegends.icon_Y_Offset.Value + (vertical ? 100 * index : 0));
        }
    }
}
