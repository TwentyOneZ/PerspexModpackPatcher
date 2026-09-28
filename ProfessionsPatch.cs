using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using Professions;
using UnityEngine;
using UnityEngine.UI;

namespace PerspexModpackPatcher;

internal static class ProfessionsPatch
{
    private const string SlotsKey = "Professions extraProfessionsSlots";
    private const string TrophyName = "craftsman trophy";
    private static readonly Type Mod = typeof(Professions.Professions);
    private static readonly FieldInfo Maximum = AccessTools.Field(Mod, "maximumAllowedProfessions");
    private static readonly FieldInfo Reset = AccessTools.Field(Mod, "resetLevelOnUnselect");
    private static readonly FieldInfo Elements = AccessTools.Field(Mod, "professionPanelElements");
    private static readonly FieldInfo Panel = AccessTools.Field(Mod, "professionPanelInstance");
    private static readonly FieldInfo Stations = AccessTools.Field(typeof(Player), "m_knownStations");
    private static readonly MethodInfo Refresh = AccessTools.Method(Mod, "UpdateSelectPanelSelections");
    private static readonly MethodInfo SkillType = AccessTools.Method(Mod, "fromProfession");
    private static readonly MethodInfo GetSkill = AccessTools.Method(typeof(Skills), "GetSkill");
    private static readonly FieldInfo SkillLevel = AccessTools.Field(typeof(Skills.Skill), "m_level");
    private static readonly int[] Costs = { 0, 2, 3, 5, 9, 16, 30, 60, 120, 240, 480, 1000 };
    private static readonly Regex SelectionCount = new(@" / \d+ professions selected\.", RegexOptions.Compiled);
    private static readonly Regex LevelSuffix = new(@"\s*\[Lv[^\]]*\]?$", RegexOptions.Compiled);
    private static readonly Dictionary<Skills.SkillType, Professions.Professions.Profession> ProfessionSkills = new();

    internal static void Install(Harmony harmony)
    {
        var panelPatch = AccessTools.Inner(Mod, "PopulateSelectPanel");
        var callback = panelPatch?.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public);
        MethodInfo click = null;
        if (callback != null)
            foreach (var type in callback)
                if (type.Name.Contains("DisplayClass0_2"))
                    click = AccessTools.Method(type, "<Postfix>b__0");
        if (click == null || Maximum == null || Reset == null || Elements == null || Panel == null || Stations == null || Refresh == null || SkillType == null || GetSkill == null || SkillLevel == null)
            throw new MissingMemberException("Professions 1.4.7 selection target changed; trophy patch not installed.");
        harmony.Patch(click, prefix: new HarmonyMethod(typeof(ProfessionsPatch), nameof(BeforeClick)),
            postfix: new HarmonyMethod(typeof(ProfessionsPatch), nameof(AfterClick)));
        harmony.Patch(Refresh, postfix: new HarmonyMethod(typeof(ProfessionsPatch), nameof(AfterRefresh)));
        foreach (Professions.Professions.Profession profession in Enum.GetValues(typeof(Professions.Professions.Profession)))
            ProfessionSkills[(Skills.SkillType)SkillType.Invoke(null, new object[] { profession })] = profession;
        harmony.Patch(AccessTools.Method(typeof(Skills), "RaiseSkill"),
            prefix: new HarmonyMethod(typeof(ProfessionsPatch), nameof(OnlyActiveExperience)));
    }

    private static bool OnlyActiveExperience(Skills.SkillType skillType)
    {
        if (Player.m_localPlayer == null || !ProfessionSkills.TryGetValue(skillType, out var profession) ||
            Helper.getActiveProfessions().Contains(profession)) return true;
        return !Professions.Professions.blockOtherProfessions.TryGetValue(profession, out var setting) ||
               setting.Value != Professions.Professions.ProfessionToggle.BlockExperience &&
               setting.Value != Professions.Professions.ProfessionToggle.BlockUsage;
    }

    private struct ClickState
    {
        internal Professions.Professions.Profession Profession;
        internal float Level;
        internal bool WasActive;
        internal bool HadInactiveLevel;
        internal bool HasValue;
    }

    private static bool BeforeClick(object __instance, out ClickState __state)
    {
        __state = default;
        var player = Player.m_localPlayer;
        if (player == null) return true;
        var locals = AccessTools.Field(__instance.GetType(), "CS$<>8__locals2")?.GetValue(__instance);
        var field = locals == null ? null : AccessTools.Field(locals.GetType(), "profession");
        if (field == null) return true;
        var profession = (Professions.Professions.Profession)field.GetValue(locals);
        var active = Helper.getActiveProfessions();
        __state = new ClickState { Profession = profession, WasActive = active.Contains(profession), HasValue = true };
        __state.HadInactiveLevel = Helper.getInactiveProfessions().TryGetValue(profession, out var previous) && previous > 0f;
        if (__state.WasActive)
        {
            var skill = (Skills.SkillType)SkillType.Invoke(null, new object[] { profession });
            __state.Level = player.GetSkills().GetSkillLevel(skill);
            return true;
        }

        var allowed = ((ConfigEntry<int>)Maximum.GetValue(null)).Value + ExtraSlots(player);
        if (active.Count < allowed) return true;
        var cost = Cost(allowed);
        var inventory = player.GetInventory();
        var trophies = new List<ItemDrop.ItemData>();
        var count = 0;
        if (inventory != null)
            for (var y = 0; y < inventory.GetHeight(); y++)
                for (var x = 0; x < inventory.GetWidth(); x++)
                {
                    var item = inventory.GetItemAt(x, y);
                    if (item?.m_shared?.m_name != null &&
                        item.m_shared.m_name.Equals(TrophyName, StringComparison.OrdinalIgnoreCase))
                    {
                        trophies.Add(item);
                        count += item.m_stack;
                    }
                }
        if (count < cost)
        {
            player.Message(MessageHud.MessageType.Center, $"You need Craftsman Trophy x {cost}.");
            __state.HasValue = false;
            return false;
        }
        var remaining = cost;
        foreach (var trophy in trophies)
        {
            var taken = Math.Min(remaining, trophy.m_stack);
            inventory.RemoveItem(trophy, taken);
            remaining -= taken;
            if (remaining == 0) break;
        }
        KnownStations(player)[SlotsKey] = ExtraSlots(player) + 1;
        player.StartEmote("cheer", true);
        player.Message(MessageHud.MessageType.Center, $"Consumed Craftsman Trophy x {cost}.");
        foreach (var prefab in new[] { "vfx_Potion_stamina_medium", "vfx_WishbonePing" })
        {
            var effect = ZNetScene.instance?.GetPrefab(prefab);
            if (effect != null) UnityEngine.Object.Instantiate(effect, player.transform.position, Quaternion.identity);
        }
        return true;
    }

    private static void AfterClick(ClickState __state)
    {
        var player = Player.m_localPlayer;
        if (!__state.HasValue || player == null) return;
        var active = Helper.getActiveProfessions();
        if (__state.WasActive && !active.Contains(__state.Profession) &&
            ((ConfigEntry<Professions.Professions.Toggle>)Reset.GetValue(null)).Value == Professions.Professions.Toggle.On)
        {
            var inactive = Helper.getInactiveProfessions();
            inactive[__state.Profession] = __state.Level * 0.5f;
            Helper.storeInactiveProfessions(inactive);
            Refresh.Invoke(null, null);
        }
        else if (!__state.WasActive && !__state.HadInactiveLevel && active.Contains(__state.Profession))
        {
            var skillType = (Skills.SkillType)SkillType.Invoke(null, new object[] { __state.Profession });
            var skill = GetSkill.Invoke(player.GetSkills(), new object[] { skillType });
            if (skill != null) SkillLevel.SetValue(skill, 1f);
            Refresh.Invoke(null, null);
        }
    }

    private static void AfterRefresh()
    {
        var player = Player.m_localPlayer;
        if (player == null) return;
        var baseSlots = ((ConfigEntry<int>)Maximum.GetValue(null)).Value;
        var allowed = baseSlots + ExtraSlots(player);
        var active = Helper.getActiveProfessions();
        var inactive = Helper.getInactiveProfessions();
        var elements = (Dictionary<Professions.Professions.Profession, GameObject>)Elements.GetValue(null);
        foreach (var pair in elements)
        {
            var element = pair.Value.GetComponent<Skill_Element>();
            if (element == null) continue;
            var selected = active.Contains(pair.Key);
            element.Toggle(selected, active.Count >= allowed);
            if (!selected && active.Count >= allowed)
            {
                element.Select.interactable = true;
                element.buttontxt.text = "Unlock";
            }
            element.m_Title.text = LevelSuffix.Replace(element.m_Title.text, "");
            var skill = (Skills.SkillType)SkillType.Invoke(null, new object[] { pair.Key });
            var level = selected ? player.GetSkills().GetSkillLevel(skill) : inactive.TryGetValue(pair.Key, out var saved) ? saved : 0f;
            ShowLevel(element, Mathf.FloorToInt(level));
        }
        var panel = ((GameObject)Panel.GetValue(null))?.GetComponent<ProfessionPanel>();
        if (panel == null) return;
        var text = panel.description.text;
        var suffixStart = text.IndexOf("\nCraftsman Trophy x ", StringComparison.Ordinal);
        if (suffixStart >= 0) text = text.Substring(0, suffixStart);
        text = SelectionCount.Replace(text, $" / {allowed} professions selected.");
        var label = ((ConfigEntry<Professions.Professions.Toggle>)Reset.GetValue(null)).Value == Professions.Professions.Toggle.On ? "50%" : "100%";
        panel.description.text = text + $"\nCraftsman Trophy x {Cost(allowed)} unlocks another profession.\nRelearn at {label} of your previous level.";
    }

    private static void ShowLevel(Skill_Element element, int level)
    {
        if (element.configOptions == null || element.configOptions.Length == 0 || element.configOptions[0] == null) return;
        var icon = element.configOptions[0];
        icon.gameObject.SetActive(true);
        icon.color = new Color(1f, 1f, 1f, 0f);
        var child = icon.transform.Find("PerspexLevel");
        if (child == null)
        {
            var label = new GameObject("PerspexLevel", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(icon.transform, false);
            label.font = element.m_Title.font;
            label.color = element.m_Title.color;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 20;
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            child = label.transform;
        }
        child.GetComponent<Text>().text = level.ToString();
    }

    private static Dictionary<string, int> KnownStations(Player player) => (Dictionary<string, int>)Stations.GetValue(player);

    private static int ExtraSlots(Player player) => KnownStations(player).TryGetValue(SlotsKey, out var slots) ? slots : 0;

    internal static int Cost(int allowed) => allowed <= 0 ? 0 : allowed < Costs.Length ? Costs[allowed] : Mathf.CeilToInt(0.97657f * Mathf.Pow(2f, allowed));
}
