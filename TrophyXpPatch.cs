using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PerspexModpackPatcher;

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class TrophyXpPatch
{
    private static readonly Dictionary<string, ConfigEntry<float>> Values = new(StringComparer.OrdinalIgnoreCase);
    private static ConfigEntry<float> multiplier;
    private static PropertyInfo epicInstance;
    private static MethodInfo addExp;

    internal static void Configure(ConfigFile config)
    {
        multiplier = config.Bind("TrophyXP", "Multiplier", 1f, "Multiplier for experience awarded by consumed creature trophies.");
        using var stream = typeof(TrophyXpPatch).Assembly.GetManifestResourceStream("PerspexModpackPatcher.TrophyXpDefaults.txt");
        if (stream == null) throw new InvalidOperationException("Trophy XP defaults are missing from the assembly.");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('=');
            if (parts.Length != 2 || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
            Values[parts[0]] = config.Bind("TrophyXP", parts[0], value, "Experience from consuming this creature trophy.");
        }
    }

    internal static void Install()
    {
        var epic = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == "EpicMMOSystem");
        var type = epic?.GetType("EpicMMOSystem.LevelSystem");
        epicInstance = type?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        addExp = type?.GetMethod("AddExp", BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(int), typeof(bool) }, null);
    }

    private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item, bool fromInventoryGui)
    {
        if (epicInstance == null || addExp == null) Install();
        if (epicInstance == null || addExp == null || __instance is not Player player ||
            player != Player.m_localPlayer || item?.m_shared?.m_name == null ||
            !item.m_shared.m_name.StartsWith("$item_trophy_", StringComparison.Ordinal)) return true;

        var source = inventory ?? player.GetInventory();
        if (source == null || !source.ContainsItem(item)) return false;
        if (!fromInventoryGui)
        {
            var target = player.GetHoverObject()?.GetComponentInParent<Interactable>();
            if (target is ItemStand || target is OfferingBowl) return true;
        }

        var levelSystem = epicInstance.GetValue(null);
        if (levelSystem == null) return true;
        var enemy = item.m_shared.m_name.Substring("$item_trophy_".Length);
        var amount = Mathf.Max(0, Mathf.RoundToInt((Values.TryGetValue(enemy, out var configured) ? configured.Value : 10f) * multiplier.Value));
        try { addExp.Invoke(levelSystem, new object[] { amount, false }); }
        catch (TargetInvocationException) { return true; }
        source.RemoveOneItem(item);
        var head = Traverse.Create(player).Field("m_head").GetValue<Transform>();
        player.m_skillLevelupEffects.Create(head != null ? head.position : player.GetHeadPoint(),
            head != null ? head.rotation : Quaternion.identity, head, 1f, -1, player.GetZDOID());
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>()?.SetTrigger("gpower");
        player.Message(MessageHud.MessageType.TopLeft, "Experience received: " + amount);
        return false;
    }
}
