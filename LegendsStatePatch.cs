using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

// Keeps the selected class in vanilla player data so server-side character storage carries it.
public static class LegendsStatePatch
{
    private const string ClassKey = "Perspex.Legends.Class";
    private static Player current;
    private static bool restored;
    private static ValheimLegends.ValheimLegends.PlayerClass activeClass;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.ValheimLegends), nameof(ValheimLegends.ValheimLegends.SetVLPlayer)),
            prefix: new HarmonyMethod(typeof(LegendsStatePatch), nameof(SetPlayer)));
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.ValheimLegends), nameof(ValheimLegends.ValheimLegends.UpdateVLPlayer)),
            prefix: new HarmonyMethod(typeof(LegendsStatePatch), nameof(UpdatePlayer)));
        var save = AccessTools.Method(typeof(Player), "Save");
        var load = AccessTools.Method(typeof(Player), "Load");
        if (save != null) harmony.Patch(save, prefix: new HarmonyMethod(typeof(LegendsStatePatch), nameof(BeforeSave)));
        if (load != null) harmony.Patch(load, postfix: new HarmonyMethod(typeof(LegendsStatePatch), nameof(AfterLoad)));
        RemoveLegacyFileHook(harmony, "SavePlayerToDisk", "SaveVLPlayer_Patch");
        RemoveLegacyFileHook(harmony, "LoadPlayerFromDisk", "LoadVLPlayer_Patch");
    }

    private static ValheimLegends.ValheimLegends.PlayerClass ReadClass(Player player)
    {
        var selected = ValheimLegends.ValheimLegends.PlayerClass.None;
        if (player.m_customData.TryGetValue(ClassKey, out var saved) &&
            int.TryParse(saved, out var value) &&
            Enum.IsDefined(typeof(ValheimLegends.ValheimLegends.PlayerClass), value))
            selected = (ValheimLegends.ValheimLegends.PlayerClass)value;
        var configured = ValheimLegends.ValheimLegends.chosenClass?.Value;
        if (Enum.TryParse(configured, true, out ValheimLegends.ValheimLegends.PlayerClass requested) &&
            requested != ValheimLegends.ValheimLegends.PlayerClass.None &&
            (selected == ValheimLegends.ValheimLegends.PlayerClass.None ||
             VL_GlobalConfigs.ConfigStrings.TryGetValue("vl_svr_enforceConfigClass", out var enforce) && enforce != 0f))
            selected = requested;
        return selected;
    }

    private static bool SetPlayer(Player p)
    {
        if (p == null || Player.m_localPlayer != null && p != Player.m_localPlayer) return false;
        var state = new ValheimLegends.ValheimLegends.VL_Player
        {
            vl_name = p.GetPlayerName(), vl_class = ReadClass(p)
        };
        ValheimLegends.ValheimLegends.vl_player = state;
        ValheimLegends.ValheimLegends.vl_playerList = new List<ValheimLegends.ValheimLegends.VL_Player> { state };
        current = p;
        restored = Player.m_localPlayer == p;
        activeClass = state.vl_class;
        p.m_customData[ClassKey] = ((int)state.vl_class).ToString();
        if (restored) ValheimLegends.ValheimLegends.NameCooldowns();
        return false;
    }

    private static bool UpdatePlayer(Player p)
    {
        if (p == null || ValheimLegends.ValheimLegends.vl_player == null) return false;
        var state = ValheimLegends.ValheimLegends.vl_player;
        if (p == Player.m_localPlayer) ClassChanged(p, state.vl_class);
        state.vl_name = p.GetPlayerName();
        ValheimLegends.ValheimLegends.vl_playerList = new List<ValheimLegends.ValheimLegends.VL_Player> { state };
        p.m_customData[ClassKey] = ((int)state.vl_class).ToString();
        return false;
    }

    private static void RemoveLegacyFileHook(Harmony harmony, string method, string patchType)
    {
        var target = AccessTools.Method(typeof(PlayerProfile), method);
        if (target == null) return;
        foreach (var patch in Harmony.GetPatchInfo(target)?.Postfixes?.ToArray() ?? Array.Empty<Patch>())
            if (patch.PatchMethod.DeclaringType?.Name == patchType &&
                patch.PatchMethod.DeclaringType.Assembly == typeof(ValheimLegends.ValheimLegends).Assembly)
                harmony.Unpatch(target, patch.PatchMethod);
    }

    internal static void Sync()
    {
        var player = Player.m_localPlayer;
        if (player == null)
        {
            current = null;
            restored = false;
            return;
        }
        if (player != current) { current = player; restored = false; }
        var state = ValheimLegends.ValheimLegends.vl_player;
        if (state == null || state.vl_name != player.GetPlayerName()) return;
        if (!restored)
        {
            restored = true;
            if (player.m_customData.TryGetValue(ClassKey, out var saved) &&
                int.TryParse(saved, out var value) && Enum.IsDefined(typeof(ValheimLegends.ValheimLegends.PlayerClass), value))
                state.vl_class = (ValheimLegends.ValheimLegends.PlayerClass)value;
            else
                state.vl_class = ValheimLegends.ValheimLegends.PlayerClass.None;
            ValheimLegends.ValheimLegends.vl_playerList = new List<ValheimLegends.ValheimLegends.VL_Player>
            {
                new ValheimLegends.ValheimLegends.VL_Player { vl_name = player.GetPlayerName(), vl_class = state.vl_class }
            };
            ValheimLegends.ValheimLegends.NameCooldowns();
            activeClass = state.vl_class;
        }
        ClassChanged(player, state.vl_class);
        player.m_customData[ClassKey] = ((int)state.vl_class).ToString();
    }

    private static void ClassChanged(Player player, ValheimLegends.ValheimLegends.PlayerClass selected)
    {
        if (selected == activeClass) return;
        activeClass = selected;
        var effects = player.GetSEMan();
        if (effects != null)
            foreach (var effect in effects.GetStatusEffects().ToArray())
                if (effect != null && effect.name != null &&
                    (effect.name.EndsWith("_CD", StringComparison.Ordinal) ||
                     effect.name.StartsWith("SE_VL_CD", StringComparison.Ordinal)) &&
                    effect.name.StartsWith("SE_VL_", StringComparison.Ordinal) && effect.m_ttl > 0f)
                    effect.m_ttl -= Mathf.Max(0f, effect.GetRemaningTime()) * 0.9f;

        foreach (var summon in Character.GetAllCharacters().ToArray())
        {
            if (summon == null || summon == player) continue;
            var statuses = summon.GetSEMan();
            var companion = statuses?.GetStatusEffect("SE_VL_Companion".GetStableHashCode()) as SE_Companion;
            var roots = statuses?.GetStatusEffect("SE_VL_RootsBuff".GetStableHashCode()) as SE_RootsBuff;
            var charm = statuses?.GetStatusEffect("SE_VL_Charm".GetStableHashCode()) as SE_Charm;
            var owner = summon.GetComponent<ZNetView>()?.GetZDO();
            var owned = companion?.summoner == player || roots?.summoner == player ||
                (companion != null || roots != null) && owner != null &&
                (owner.GetZDOID("VL_Companion_Summoner") == player.GetZDOID() ||
                 owner.GetZDOID("VL_SummonOwner") == player.GetZDOID());
            if (owned)
            {
                if (summon.GetBaseAI() is MonsterAI ai) ai.SetFollowTarget(null);
                var hit = new HitData();
                hit.m_damage.m_spirit = 999999f;
                summon.ApplyDamage(hit, false, false, HitData.DamageModifier.VeryWeak);
                if (Class_Ranger.GO_Wolf == summon.gameObject) Class_Ranger.GO_Wolf = null;
            }
            else if (charm?.summoner == player)
            {
                summon.m_faction = charm.originalFaction;
                summon.SetTamed(false);
                statuses.RemoveStatusEffect(charm, true);
            }
        }
    }

    private static void BeforeSave(Player __instance)
    {
        if (__instance == Player.m_localPlayer) Sync();
    }

    public static void ApplyAuthorityClass(Player player, int classId)
    {
        if (player == null || !Enum.IsDefined(typeof(ValheimLegends.ValheimLegends.PlayerClass), classId)) return;
        var selected = (ValheimLegends.ValheimLegends.PlayerClass)classId;
        player.m_customData[ClassKey] = classId.ToString();
        if (player != Player.m_localPlayer) return;
        var state = ValheimLegends.ValheimLegends.vl_player;
        if (state == null || state.vl_name != player.GetPlayerName()) return;
        state.vl_class = selected;
        activeClass = selected;
        current = player;
        restored = true;
        ValheimLegends.ValheimLegends.NameCooldowns();
    }

    private static void AfterLoad(Player __instance)
    {
        if (__instance == Player.m_localPlayer) { current = null; restored = false; Sync(); }
    }
}
