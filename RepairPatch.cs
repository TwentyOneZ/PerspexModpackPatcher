using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PerspexModpackPatcher;

internal static class RepairPatch
{
    internal static ConfigEntry<float> CoinMultiplier;
    [ThreadStatic] private static bool repairing;
    [ThreadStatic] private static ItemDrop.ItemData selected;
    [ThreadStatic] private static int selectedCost;
    private static string repairTooltip;

    [HarmonyPatch(typeof(InventoryGui), "RepairOneItem")]
    private static class RepairScope
    {
        private static void Prefix() { repairing = true; selected = null; selectedCost = 0; }
        private static void Postfix()
        {
            if (selected != null && selected.m_durability >= selected.GetMaxDurability() && Player.m_localPlayer != null)
                Player.m_localPlayer.GetInventory().RemoveItem("$item_coins", selectedCost);
        }
        private static void Finalizer() { repairing = false; selected = null; selectedCost = 0; }
    }

    [HarmonyPatch(typeof(InventoryGui), "CanRepair")]
    private static class RepairCost
    {
        private static void Postfix(ItemDrop.ItemData item, ref bool __result)
        {
            if (!repairing || !__result || item == null || Player.m_localPlayer == null ||
                Player.m_localPlayer.NoCostCheat()) return;
            var cost = CoinsNeeded(item);
            if (cost == 0) return;
            var inventory = Player.m_localPlayer.GetInventory();
            if (inventory.CountItems("$item_coins") < cost)
            {
                __result = false;
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Repair requires " + cost + " coins.");
                return;
            }
            selected = item;
            selectedCost = cost;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
    private static class DescribeRepairs
    {
        private static void Postfix(List<ItemDrop.ItemData> ___m_tempWornItems)
        {
            var player = Player.m_localPlayer;
            if (player == null || ___m_tempWornItems == null || ___m_tempWornItems.Count == 0)
            {
                repairTooltip = null;
                return;
            }
            var lines = new List<string> { "<b>" + Localization.instance.Localize("$inventory_repairbutton") + "</b>" };
            var coins = player.GetInventory().CountItems("$item_coins");
            foreach (var item in ___m_tempWornItems)
            {
                if (item?.m_shared == null) continue;
                var cost = player.NoCostCheat() ? 0 : CoinsNeeded(item);
                var color = cost <= coins ? "white" : "red";
                lines.Add("<color=" + color + ">" + Localization.instance.Localize(item.m_shared.m_name) +
                          ": " + (cost == 0 ? "Free" : cost + " coins") + "</color>");
            }
            repairTooltip = string.Join("\n", lines);
        }
    }

    [HarmonyPatch(typeof(UITooltip), "LateUpdate")]
    private static class ShowRepairTooltip
    {
        private static void Postfix(UITooltip __instance, UITooltip ___m_current, GameObject ___m_tooltip)
        {
            if (repairTooltip == null || __instance != ___m_current || ___m_tooltip == null) return;
            var button = Traverse.Create(InventoryGui.instance).Field("m_repairButton").GetValue<Button>();
            if (button == null || button.GetComponent<UITooltip>() != __instance) return;
            var tmp = ___m_tooltip.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null) { tmp.richText = true; tmp.text = repairTooltip; return; }
            var plain = ___m_tooltip.GetComponentInChildren<Text>(true);
            if (plain != null) { plain.supportRichText = true; plain.text = repairTooltip; }
        }
    }

    internal static int CoinsNeeded(ItemDrop.ItemData item)
    {
        var maxDurability = item.GetMaxDurability();
        if (maxDurability <= 0 || ObjectDB.instance == null) return 0;
        var recipe = ObjectDB.instance.GetRecipe(item);
        if (recipe?.m_resources == null) return 0;
        var baseCoins = 0;
        foreach (var resource in recipe.m_resources)
        {
            if (resource?.m_resItem?.m_itemData?.m_shared?.m_name != "$item_coins") continue;
            for (var level = 1; level <= item.m_quality; level++) baseCoins += resource.GetAmount(level);
        }
        var wear = Mathf.Clamp01(1f - item.m_durability / maxDurability);
        return Mathf.Max(0, Mathf.RoundToInt(baseCoins * wear * CoinMultiplier.Value));
    }
}
