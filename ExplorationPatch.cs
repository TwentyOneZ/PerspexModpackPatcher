using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace PerspexModpackPatcher;

internal static class ExplorationPatch
{
    private static ConfigEntry<int> minimapLevel;
    private static ConfigEntry<int> treasureLevel;
    private static ConfigEntry<int> treasureChance;
    private static float nextCheck;
    private static float nextNotice;
    private static bool treasurePatchSafe = true;

    internal static void Configure(ConfigFile config)
    {
        minimapLevel = config.Bind("Exploration", "MinimapUnlockLevel", 40, "Exploration level required for the minimap.");
        treasureLevel = config.Bind("Exploration", "TreasureLevel", 50, "Exploration level needed to multiply treasure.");
        treasureChance = config.Bind("Exploration", "TreasureChance", 25, "Percent chance to multiply treasure once per chest.");
    }

    internal static void RemoveConflictingPatches(Harmony harmony)
    {
        Remove("org.bepinex.plugins.professions", "BlockExploration", AccessTools.DeclaredMethod(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) }));
        var explorationInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.exploration");
        treasurePatchSafe = !explorationInstalled || Remove("org.bepinex.plugins.exploration", "MultiplyTreasure", AccessTools.Method(typeof(Container), "RPC_OpenResponse"));
        bool Remove(string owner, string typeName, System.Reflection.MethodBase target)
        {
            if (target == null) return false;
            foreach (var patch in Harmony.GetPatchInfo(target)?.Prefixes ?? Enumerable.Empty<Patch>())
                if (patch.owner == owner && patch.PatchMethod.DeclaringType?.Name == typeName)
                {
                    harmony.Unpatch(target, patch.PatchMethod);
                    return true;
                }
            return false;
        }
    }

    private static bool HasExploration(Player player)
    {
        if (player == null) return false;
        if (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.professions")) return true;
        if (player.m_customData.TryGetValue("Professions Active", out var active) &&
            active.Split(',').Any(name => name.Trim() == "Exploration")) return true;
        return Professions.Professions.blockOtherProfessions.TryGetValue(
            Professions.Professions.Profession.Exploration, out var setting) &&
            setting.Value != Professions.Professions.ProfessionToggle.BlockUsage;
    }

    private static bool SelectedExploration(Player player) =>
        player != null && (!BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("org.bepinex.plugins.professions") ||
            player.m_customData.TryGetValue("Professions Active", out var active) &&
            active.Split(',').Any(name => name.Trim() == "Exploration"));

    private static float Skill(Player player, string name) =>
        player.GetSkills().GetSkillLevel((Skills.SkillType)Math.Abs(name.GetStableHashCode()));

    private static bool Small(Player player) =>
        SelectedExploration(player) && Skill(player, "Exploration") >= minimapLevel.Value;

    private static bool Large(Player player) => SelectedExploration(player) ||
        (player.IsSitting() &&
            EffectArea.IsPointInsideArea(player.transform.position, EffectArea.Type.Fire, 0f) != null);

    private static void Denied(Player player)
    {
        if (Time.unscaledTime < nextNotice) return;
        nextNotice = Time.unscaledTime + 1.5f;
        player.Message(MessageHud.MessageType.Center, "Select Exploration or sit near a fire to open the map.");
    }

    private static bool MapPressed() => ZInput.GetButtonDown("Map") ||
        (ZInput.GetButtonDown("JoyMap") && (!ZInput.GetButton("JoyLTrigger") || !ZInput.GetButton("JoyLBumper")) &&
         !ZInput.GetButton("JoyAltKeys"));

    [HarmonyPatch(typeof(MapTable), "OnWrite")]
    private static class WriteMap
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => Player.m_localPlayer != null && HasExploration(Player.m_localPlayer) &&
            Skill(Player.m_localPlayer, "Exploration") >= 20;
    }

    [HarmonyPatch(typeof(MapTable), "OnRead", new[] { typeof(Switch), typeof(Humanoid), typeof(ItemDrop.ItemData), typeof(bool) })]
    private static class ReadMap
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => Player.m_localPlayer != null && HasExploration(Player.m_localPlayer) &&
            Skill(Player.m_localPlayer, "Exploration") >= 40;
    }

    [HarmonyPatch(typeof(Minimap), "SetMapMode")]
    private static class MapMode
    {
        private static void Prefix(Minimap __instance, ref Minimap.MapMode mode)
        {
            var player = Player.m_localPlayer;
            if (player == null || Game.m_noMap) return;
            if (mode == Minimap.MapMode.Small && !Small(player))
            {
                mode = Large(player) && MapPressed() ? Minimap.MapMode.Large : Minimap.MapMode.None;
                if (mode == Minimap.MapMode.None && MapPressed()) Denied(player);
            }
            else if (mode == Minimap.MapMode.Large && !Large(player))
            {
                mode = Small(player) ? Minimap.MapMode.Small : Minimap.MapMode.None;
                if (MapPressed()) Denied(player);
            }
        }
    }

    [HarmonyPatch(typeof(Minimap), "Update")]
    private static class EnforceAccess
    {
        private static void Postfix(Minimap __instance)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead() || Game.m_noMap || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            if (__instance.m_mode == Minimap.MapMode.Large && !Large(player))
                __instance.SetMapMode(Small(player) ? Minimap.MapMode.Small : Minimap.MapMode.None);
            else if (__instance.m_mode == Minimap.MapMode.None && Small(player))
                __instance.SetMapMode(Minimap.MapMode.Small);
            else if (__instance.m_mode == Minimap.MapMode.Small && !Small(player))
                __instance.SetMapMode(Minimap.MapMode.None);
        }
    }

    [HarmonyPatch(typeof(Container), "RPC_OpenResponse")]
    private static class Treasure
    {
        private static void Prefix(Container __instance, bool granted)
        {
            var player = Player.m_localPlayer;
            if (!treasurePatchSafe || !granted || player == null || !__instance.name.StartsWith("TreasureChest_", StringComparison.Ordinal)) return;
            var zdo = Traverse.Create(__instance).Field("m_nview").GetValue<ZNetView>()?.GetZDO();
            if (zdo == null || zdo.GetBool("Exploration Treasure Looted", false)) return;
            zdo.Set("Exploration Treasure Looted", true);
            if (HasExploration(player) && Skill(player, "Exploration") >= Math.Max(50, treasureLevel.Value) &&
                UnityEngine.Random.Range(0, 100) < treasureChance.Value)
                foreach (var item in __instance.GetInventory().GetAllItems().ToArray())
                    __instance.GetInventory().AddItem(item.m_dropPrefab, item.m_stack);
            if (HasExploration(player))
                player.RaiseSkill((Skills.SkillType)Math.Abs("Exploration".GetStableHashCode()), 35f);
        }
    }
}
