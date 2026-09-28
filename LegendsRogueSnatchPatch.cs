using System;
using System.Collections.Generic;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsRogueSnatchPatch
{
    private static readonly HashSet<int> Snatched = new();
    private static Player owner;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsRogueSnatchPatch), nameof(OnHit)));
        harmony.Patch(AccessTools.Method(typeof(Player), "Update"),
            postfix: new HarmonyMethod(typeof(LegendsRogueSnatchPatch), nameof(ClearWhenNotRogue)));
    }

    private static void ClearWhenNotRogue(Player __instance)
    {
        if (__instance != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Rogue) return;
        Snatched.Clear();
        owner = null;
    }

    private static void OnHit(Character __instance, HitData hit)
    {
        if (hit == null || __instance == null || __instance.IsPlayer() || hit.m_ranged ||
            hit.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Rogue)
            return;
        if (owner != player) { owner = player; Snatched.Clear(); }
        if (Snatched.Contains(__instance.GetInstanceID())) return;
        var weapon = player.GetCurrentWeapon();
        var shared = weapon?.m_shared;
        if (shared == null) return;
        var left = Traverse.Create(player).Field("m_leftItem").GetValue<ItemDrop.ItemData>();
        var unarmed = string.Equals(shared.m_name, "Unarmed", StringComparison.OrdinalIgnoreCase);
        if (!unarmed && (left != null || shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon &&
                          shared.m_attachOverride != ItemDrop.ItemData.ItemType.Hands)) return;
        if (player.GetSEMan().GetStatusEffect("SE_VL_Rogue".GetStableHashCode()) is not SE_Rogue rogue ||
            rogue.hitCount <= 0) return;
        var amount = Mathf.CeilToInt(UnityEngine.Random.Range(0.33f, 1f) *
            (1f + LevelSystem.Instance.getAddCriticalChance() / 40f) *
            Mathf.Sqrt(__instance.GetMaxHealth()));
        if (!LegendsDuelistChallengePatch.AwardCoins(player, amount)) return;
        rogue.hitCount--;
        Snatched.Add(__instance.GetInstanceID());
        player.Message(MessageHud.MessageType.TopLeft,
            $"Snatched {amount} coins from {__instance.GetHoverName()}!");
        if (Snatched.Count > 500) Snatched.Clear();
    }
}
