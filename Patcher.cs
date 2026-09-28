using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace PerspexModpackPatcher;

[BepInPlugin("twentyonez.perspex.patcher", "Perspex Modpack Patcher", "0.2.20")]
[BepInDependency(Jotunn.Main.ModGuid)]
[BepInDependency("ValheimLegends")]
public sealed class Patcher : BaseUnityPlugin
{
    private Harmony harmony;
    private Action syncLegends;
    internal static ConfigEntry<bool> RequireResting;
    internal static ConfigEntry<bool> AllowTargeted;
    internal static ConfigEntry<bool> AllowRestricted;
    internal static ConfigEntry<bool> AllowEncumbered;
    internal static ConfigEntry<bool> AllowInWater;
    private ConfigEntry<bool> registerFallbackItems;

    private void Awake()
    {
        RequireResting = Config.Bind("Hearthstone", "RequireResting", true, "Require the Resting effect to use a teleport stone.");
        AllowTargeted = Config.Bind("Hearthstone", "AllowTargeted", true, "Allow teleporting while targeted by enemies.");
        AllowRestricted = Config.Bind("Hearthstone", "AllowRestricted", false, "Allow teleporting with portal restricted inventory.");
        AllowEncumbered = Config.Bind("Hearthstone", "AllowEncumbered", false, "Allow teleporting while encumbered.");
        AllowInWater = Config.Bind("Hearthstone", "AllowInWater", false, "Allow teleporting while in water.");
        registerFallbackItems = Config.Bind("Items", "RegisterFallbackItems", false,
            "Create basic slot trophies and Deathstone when WackysDatabase item definitions are not installed. Hearthstone and Marketstone are always registered.");
        RepairPatch.CoinMultiplier = Config.Bind("Repair", "CoinMultiplier", 0.5f, "Fraction of the recipe's coin cost used for a full repair, scaled by wear.");
        TrophyXpPatch.Configure(Config);
        ExplorationPatch.Configure(Config);
        HotbarDiagnostics.Configure(Config, Logger);
        harmony = new Harmony("twentyonez.perspex.patcher");
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Patcher).Assembly))
            if (type.Namespace == typeof(Patcher).Namespace && type.IsDefined(typeof(HarmonyPatch), false))
                harmony.CreateClassProcessor(type).Patch();
        PrefabManager.OnVanillaPrefabsAvailable += RegisterItems;
        Logger.LogInfo("Perspex Modpack Patcher 0.2.20 loaded");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    private void Start()
    {
        TrophyXpPatch.Install();
        ExplorationPatch.RemoveConflictingPatches(harmony);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name).ToArray();
        if (AccessTools.TypeByName("MyriadJewels.HerbPlantableFix") != null &&
            !loaded.Contains("MyriadJewelsPerformancePatch") &&
            Install("MyriadJewels prefab search", () => MyriadJewelsPerformancePatch.Install(harmony)))
            Logger.LogInfo("MyriadJewels sage/mint prefab search limited to one global scan per 90 frames.");
        if (loaded.Contains("EpicMMOSystem"))
            Install("EpicMMO levels", () => EpicMmoPatch.Install(harmony));
        if (loaded.Contains("ValheimLegends"))
        {
            var version = typeof(ValheimLegends.ValheimLegends)
                .GetCustomAttributes(typeof(BepInPlugin), false).OfType<BepInPlugin>()
                .FirstOrDefault()?.Version;
            if (version?.ToString() == "0.7.10")
            {
                Install("Legends HUD", () => LegendsHudPatch.Install(harmony));
                if (Install("Legends class storage", () => LegendsStatePatch.Install(harmony)))
                    syncLegends = () => { LegendsStatePatch.Sync(); LegendsDruidRootsPatch.Sync(); };
                Install("Legends economy", () => LegendsEconomyPatch.Install(harmony));
                Install("Legends translocation", () => LegendsTranslocationPatch.Install(harmony));
                Install("Legends Shaman", () => LegendsShamanPatch.Install(harmony));
                Install("Legends Druid", () => LegendsDruidPatch.Install(harmony));
                Install("Legends Fenring", () => LegendsFenringPatch.Install(harmony));
                Install("Legends Druid Roots", () => LegendsDruidRootsPatch.Install(harmony));
                Install("Legends Priest", () => LegendsPriestPatch.Install(harmony));
                Install("Legends Mage", () => LegendsMageDamagePatch.Install(harmony));
                Install("Legends Mage affinities", () => LegendsMageAffinityPatch.Install(harmony));
                Install("Legends Mage Frost", () => LegendsMageFrostPatch.Install(harmony));
                Install("Legends Mage Arcane", () => LegendsMageArcanePatch.Install(harmony));
                Install("Legends Enchanter imbuements", () => LegendsEnchanterImbuePatch.Install(harmony));
                Install("Legends Charm", () => LegendsCharmPatch.Install(harmony));
                Install("Legends Windfury", () => LegendsWindfuryPatch.Install(harmony));
                Install("Legends Rooted", () => LegendsRootedPatch.Install(harmony));
                Install("Legends environment", () => LegendsEnvironmentPatch.Install(harmony));
                Install("Legends stealth", () => LegendsStealthPatch.Install(harmony));
                Install("Legends class passives", () => LegendsClassPassivesPatch.Install(harmony));
                Install("Legends Duelist challenge", () => LegendsDuelistChallengePatch.Install(harmony));
                Install("Legends Rogue snatch", () => LegendsRogueSnatchPatch.Install(harmony));
                Install("Legends Rogue", () => LegendsRogueDamagePatch.Install(harmony));
                Install("Legends caster damage", () => LegendsCasterDamagePatch.Install(harmony));
                Install("Legends Reactive Armor", () => LegendsReactiveArmorPatch.Install(harmony));
                Install("Legends martial damage", () => LegendsMartialDamagePatch.Install(harmony));
                Install("Legends dual wield stamina", () => LegendsDualWieldStaminaPatch.Install(harmony));
                Install("Legends Monk and Valkyrie", () => LegendsMonkValkyriePatch.Install(harmony));
                Install("Legends Spirit Drain", () => LegendsSpiritDrainPatch.Install(harmony));
                Install("Legends Power Shot", () => LegendsPowerShotPatch.Install(harmony));
                Install("Legends Ranger", () => LegendsRangerPatch.Install(harmony));
                Install("Legends Execute", () => LegendsExecutePatch.Install(harmony));
                Install("Legends Berserk", () => LegendsBerserkStatusPatch.Install(harmony));
                Install("Legends Seed Regeneration", () => LegendsSeedPatch.Install(harmony));
                Install("Legends summons", () => LegendsSummonPatch.Install(harmony));
                Install("Legends companions", () => LegendsCompanionPatch.Install(harmony));
                Install("Legends class statuses", () => LegendsClassStatusPatch.Install(harmony));
                Install("Legends Shaman statuses", () => LegendsShamanStatusPatch.Install(harmony));
                Install("Legends minor statuses", () => LegendsMinorStatusPatch.Install(harmony));
                Install("Legends Monk Surge", () => LegendsMonkSurgePatch.Install(harmony));
                Install("Legends Monk Power Up", () => LegendsMonkPowerUpPatch.Install(harmony));
                Install("Legends class repairs", () => LegendsClassRepairPatch.Install(harmony));
                Install("Legends Munin tutorials", () => LegendsMuninPatch.Install(harmony));
                Install("Legends Monk damage", () => LegendsMonkDamagePatch.Install(harmony));
                Install("Legends Riposte", () => LegendsRipostePatch.Install(harmony));
            }
            else Logger.LogError($"Valheim Legends {version?.ToString() ?? "unknown"} is installed; Perspex patches require Dekas 0.7.10.");
        }
        if (loaded.Contains("Professions"))
            Install("Professions", () => ProfessionsPatch.Install(harmony));
    }

    private bool Install(string name, Action action)
    {
        try { action(); return true; }
        catch (Exception error) { Logger.LogError($"{name} patch failed: {error}"); return false; }
    }

    private void RegisterItems()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= RegisterItems;
        Register("Hearthstone", "Hearthstone", "Returns to your chosen bed.", icon: "Hearthstone");
        Register("Marketstone", "Marketstone", "Returns to the discovered merchant.", icon: "Marketstone");
        if (!registerFallbackItems.Value) return;
        Register("Deathstone", "Deathstone", "Returns to your last death.", "Stone", 2, "Coins", 10, "Carrot", 5);
        Register("ExtraRowTrophy", "Inventory Expansion Trophy", "Unlocks an inventory row.");
        Register("QuickSlotsTrophy", "Quick Slots Trophy", "Unlocks a quick slot.");
        Register("LightenedSlotsTrophy", "Lightweight Trophy", "Unlocks lightweight slots.");
        Register("AmmoSlotTrophy", "Ammo Slot Trophy", "Unlocks an ammo slot.");
        Register("MiscSlotTrophy", "Misc Slot Trophy", "Unlocks a misc slot.");
        Register("FoodSlotTrophy", "Food Slot Trophy", "Unlocks a food slot.");
        Register("UtilitySlotTrophy", "Utility Slot Trophy", "Unlocks a utility slot.");
    }

    private static void Register(string prefab, string displayName, string description,
        string item1 = null, int amount1 = 0, string item2 = null, int amount2 = 0,
        string item3 = null, int amount3 = 0, string icon = null)
    {
        var config = new ItemConfig { Name = displayName, Description = description,
            StackSize = icon == null ? 20 : 10 };
        if (icon != null) config.Icons = new[] { LoadIcon(icon) };
        if (item1 != null) config.AddRequirement(item1, amount1);
        if (item2 != null) config.AddRequirement(item2, amount2);
        if (item3 != null) config.AddRequirement(item3, amount3);
        var custom = new CustomItem(prefab, "Thunderstone", config);
        custom.ItemDrop.m_itemData.m_shared.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
        custom.ItemDrop.m_itemData.m_shared.m_teleportable = true;
        custom.ItemDrop.m_itemData.m_dropPrefab = custom.ItemDrop.gameObject;
        Jotunn.Managers.ItemManager.Instance.AddItem(custom);
    }

    private static Sprite LoadIcon(string name)
    {
        using var stream = typeof(Patcher).Assembly.GetManifestResourceStream($"PerspexModpackPatcher.Assets.{name}.png")
            ?? throw new FileNotFoundException($"Missing {name} icon resource");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2);
        if (!ImageConversion.LoadImage(texture, buffer.ToArray())) throw new IOException($"Invalid {name} icon");
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    private void Update()
    {
        syncLegends?.Invoke();
        if (!Input.GetKeyDown(KeyCode.P)) return;
        var player = Player.m_localPlayer;
        var bed = player?.GetHoverObject()?.GetComponentInParent<Bed>();
        if (bed == null || player == null) return;
        var bedZdo = bed.GetComponent<ZNetView>()?.GetZDO();
        if (bedZdo == null || (bedZdo.GetLong("owner", 0L) != player.GetPlayerID() && !bed.IsCurrent())) return;
        HearthstonePatch.SetHome(player, bed.transform.position + Vector3.up);
        player.Message(MessageHud.MessageType.Center, "Hearthstone destination set.");
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UseItem))]
internal static class ConsumablePatch
{
    // Names are matched against Wacky's Database items supplied by the modpack.
    private static bool Prefix(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
    {
        if (__instance is not Player player || player != Player.m_localPlayer || item?.m_shared == null)
            return true;

        var name = item.m_shared.m_name;
        var key = TrophyKey(name, player);
        if (key != null)
        {
            if (key.Length == 0)
            {
                player.Message(MessageHud.MessageType.TopLeft, "All slots of this type are unlocked.");
                return false;
            }
            player.AddUniqueKey(key);
            (inventory ?? player.GetInventory()).RemoveItem(item, 1);
            player.Message(MessageHud.MessageType.TopLeft, "Unlocked: " + key);
            return false;
        }

        if (name != "Hearthstone" && name != "Deathstone" && name != "LastDeathStone" && name != "Marketstone")
            return true;

        Vector3 destination;
        if (name == "Hearthstone")
            destination = HearthstonePatch.GetHome(player);
        else if (name == "Deathstone" || name == "LastDeathStone")
            destination = HearthstonePatch.GetDeath(player);
        else
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetLocationIcon("Vendor_BlackForest", out destination))
            {
                player.Message(MessageHud.MessageType.Center, "Find the merchant first.");
                return false;
            }
            destination += Vector3.up;
        }

        if (destination == Vector3.zero)
        {
            player.Message(MessageHud.MessageType.Center, "No destination recorded.");
            return false;
        }
        if ((!Patcher.AllowRestricted.Value && !player.IsTeleportable(false)) ||
            (!Patcher.AllowEncumbered.Value && player.IsEncumbered()) ||
            (!Patcher.AllowTargeted.Value && player.IsTargeted()) ||
            (!Patcher.AllowInWater.Value && player.InWater()) ||
            (Patcher.RequireResting.Value && !player.GetSEMan().HaveStatusEffect("Resting".GetStableHashCode())))
        {
            player.Message(MessageHud.MessageType.Center, "You cannot teleport now.");
            return false;
        }
        if (!player.TeleportTo(destination, player.transform.rotation, true))
            return false;
        (inventory ?? player.GetInventory()).RemoveItem(item, 1);
        return false;
    }

    private static string TrophyKey(string name, Player player)
    {
        switch (name)
        {
            case "Ammo Slot Trophy": return Next(player, "AmmoSlots", 1);
            case "Misc Slot Trophy": return Next(player, "MiscSlots", 1);
            case "Food Slot Trophy": return Next(player, "FoodSlots", 1);
            case "Utility Slot Trophy": return Next(player, "UtilitySlot", 4);
            case "Inventory Expansion Trophy":
            case "$item_extrarowtrophy": return Next(player, "PlayerKeyExtraRow", 5);
            case "Quick Slots Trophy":
            case "$item_quickslotstrophy": return Next(player, "PlayerKeyQuickSlots", 6);
            case "Lightweight Trophy":
            case "$item_lightenedslotstrophy": return Next(player, "PlayerKeyLightenedSlots", 1);
            default: return null;
        }
    }

    private static string Next(Player player, string prefix, int max)
    {
        for (var level = 1; level <= max; level++)
            if (!player.HaveUniqueKey(prefix + level))
                return prefix + level;
        return "";
    }
}

internal static class HearthstonePatch
{
    private const string HomeKey = "HS_HearthPos";
    private const string DeathKey = "Perspex_LastDeathPos";

    internal static Vector3 GetHome(Player player) => Get(player, HomeKey);
    internal static Vector3 GetDeath(Player player) => Get(player, DeathKey);
    internal static void SetHome(Player player, Vector3 position) => Save(player, HomeKey, position);

    private static Vector3 Get(Player player, string key)
    {
        var position = Vector3.zero;
        if (player.m_customData.TryGetValue(key, out var saved) && TryParsePosition(saved, out position))
            return position;
        var zdo = player.GetComponent<ZNetView>()?.GetZDO();
        position = zdo?.GetVec3(key, Vector3.zero) ?? Vector3.zero;
        if (position != Vector3.zero) return position;
        try
        {
            var file = PositionFile(player, key);
            if (!File.Exists(file)) return Vector3.zero;
            var legacy = File.ReadAllText(file);
            if (!TryParsePosition(legacy, out position)) return Vector3.zero;
            player.m_customData[key] = legacy;
            return position;
        }
        catch (IOException) { return Vector3.zero; }
    }

    private static bool TryParsePosition(string text, out Vector3 position)
    {
        position = Vector3.zero;
        var parts = text?.Split('|');
        if (parts?.Length != 3 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
            !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
            !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) return false;
        position = new Vector3(x, y, z);
        return true;
    }

    private static void Save(Player player, string key, Vector3 value)
    {
        var view = player.GetComponent<ZNetView>();
        if (view == null || !view.IsOwner()) return;
        view.GetZDO()?.Set(key, value);
        var encoded = string.Join("|", value.x.ToString("R", CultureInfo.InvariantCulture),
            value.y.ToString("R", CultureInfo.InvariantCulture), value.z.ToString("R", CultureInfo.InvariantCulture));
        player.m_customData[key] = encoded;
        try
        {
            var file = PositionFile(player, key);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, encoded);
        }
        catch (IOException) { }
    }

    private static string PositionFile(Player player, string key) => Path.Combine(Paths.ConfigPath, "Hearthstone",
        (key == HomeKey ? "HSPosition_" : "HSLastDeath_") + player.GetPlayerID().ToString(CultureInfo.InvariantCulture) + ".txt");

    [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
    private static class BedHint
    {
        private static void Postfix(Bed __instance, ref string __result)
        {
            var player = Player.m_localPlayer;
            var zdo = __instance.GetComponent<ZNetView>()?.GetZDO();
            if (player != null && zdo != null &&
                (zdo.GetLong("owner", 0L) == player.GetPlayerID() || __instance.IsCurrent()))
                __result += "\n[<color=yellow><b>P</b></color>] Set hearthstone";
        }
    }

    [HarmonyPatch(typeof(ItemStand), "CanAttach")]
    private static class Stand
    {
        private static void Postfix(ItemDrop.ItemData item, ref bool __result)
        {
            if (!__result && item?.m_shared?.m_name == "Hearthstone") __result = true;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    private static class Death
    {
        private static void Prefix(Player __instance)
        {
            if (__instance == Player.m_localPlayer) Save(__instance, DeathKey, __instance.transform.position);
        }
    }

}
