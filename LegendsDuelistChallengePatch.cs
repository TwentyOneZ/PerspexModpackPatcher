using System;
using System.Collections.Generic;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDuelistChallengePatch
{
    private static readonly HashSet<int> Death = new();
    private static readonly HashSet<int> Mastery = new();
    private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small",
        "piece_nonsolid", "terrain", "vehicle", "piece", "viewblock", "character", "character_net");

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Duelist), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsDuelistChallengePatch), nameof(Input))
            { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsDuelistChallengePatch), nameof(OnHit)));
        harmony.Patch(AccessTools.Method(typeof(Character), "CheckDeath"),
            prefix: new HarmonyMethod(typeof(LegendsDuelistChallengePatch), nameof(BeforeDeath)),
            postfix: new HarmonyMethod(typeof(LegendsDuelistChallengePatch), nameof(AfterDeath)));
        harmony.Patch(AccessTools.Method(typeof(Player), "Update"),
            postfix: new HarmonyMethod(typeof(LegendsDuelistChallengePatch), nameof(ClearWhenNotDuelist)));
    }

    private static void ClearWhenNotDuelist(Player __instance)
    {
        if (__instance != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Duelist) return;
        Death.Clear();
        Mastery.Clear();
    }

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability1_Input_Down) return true;
        if (player.GetSEMan().HaveStatusEffect("SE_VL_Ability1_CD".GetStableHashCode()) ||
            player.GetStamina() < VL_Utility.GetQuickShotCost) return false;
        if (player.IsBlocking()) { Challenge(player); return false; }
        if (!TakeCoins(player, 1))
        {
            player.Message(MessageHud.MessageType.TopLeft, "You need one coin to shoot.");
            return false;
        }
        var shotSound = ZNetScene.instance?.GetPrefab("sfx_coins_destroyed");
        if (shotSound != null) UnityEngine.Object.Instantiate(shotSound, player.GetCenterPoint(), Quaternion.identity);
        return true;
    }

    private static bool TakeCoins(Player player, int count)
    {
        var inventory = player.GetInventory();
        if (inventory == null || inventory.CountItems("$item_coins") < count) return false;
        for (var n = 0; n < count; n++)
        {
            ItemDrop.ItemData coin = null;
            foreach (var item in inventory.GetAllItems())
                if (item.m_shared?.m_name == "$item_coins") { coin = item; break; }
            if (coin == null) return false;
            inventory.RemoveOneItem(coin);
        }
        return true;
    }

    private static void Challenge(Player player)
    {
        if (!Physics.SphereCast(player.GetEyePoint(), 0.2f, player.GetLookDir(), out var ray, 200f, Mask) ||
            ray.collider == null)
        {
            player.Message(MessageHud.MessageType.TopLeft, "No target");
            return;
        }
        var target = ray.collider.GetComponentInParent<Character>();
        if (target == null || target.IsPlayer())
        {
            player.Message(MessageHud.MessageType.TopLeft, "Invalid target");
            return;
        }
        if (Vector3.Distance(player.transform.position, target.transform.position) > 70f)
        {
            player.Message(MessageHud.MessageType.TopLeft, target.GetHoverName() + " is too far away to challenge!");
            return;
        }
        var id = target.GetInstanceID();
        if (Death.Contains(id) || Mastery.Contains(id))
        {
            player.Message(MessageHud.MessageType.TopLeft, target.GetHoverName() + " was already challenged!");
            return;
        }
        var stake = Mathf.CeilToInt(Mathf.Sqrt(target.GetMaxHealth()));
        if (!TakeCoins(player, stake))
        {
            player.Message(MessageHud.MessageType.TopLeft,
                $"You need {stake} coins to challenge {target.GetHoverName()}.");
            return;
        }
        player.UseStamina(VL_Utility.GetQuickShotCost);
        player.StartEmote("point");
        var rich = stake >= LevelSystem.Instance.getLevel();
        var position = target.GetCenterPoint();
        var sound = ZNetScene.instance?.GetPrefab(rich ? "sfx_coins_pile_destroyed" : "sfx_coins_destroyed");
        if (sound != null) UnityEngine.Object.Instantiate(sound, position, Quaternion.identity);
        var visual = ZNetScene.instance?.GetPrefab(rich ? "vfx_coin_pile_destroyed" : "vfx_coin_stack_destroyed");
        if (visual != null) UnityEngine.Object.Instantiate(visual, position, Quaternion.identity);
        if (!target.IsOwner() || UnityEngine.Random.value < 0.33f)
        {
            Mastery.Add(id);
            player.Message(MessageHud.MessageType.Center, "Duel of Mastery!");
        }
        else
        {
            Death.Add(id);
            player.Message(MessageHud.MessageType.Center, "Duel to the Death!");
        }
        var cooldown = ScriptableObject.CreateInstance<SE_Ability1_CD>();
        cooldown.m_ttl = VL_Utility.GetQuickShotCooldownTime * 3f;
        player.GetSEMan().AddStatusEffect(cooldown);
        player.RaiseSkill(ValheimLegends.ValheimLegends.DisciplineSkill, VL_Utility.GetQuickShotSkillGain);
        player.Message(MessageHud.MessageType.TopLeft,
            $"Challenged {target.GetHoverName()} to a duel worth {stake} coins!");
        if (Death.Count + Mastery.Count > 50) { Death.Clear(); Mastery.Clear(); }
    }

    private static void OnHit(Character __instance, HitData hit)
    {
        if (__instance == null || hit?.GetAttacker() != Player.m_localPlayer || hit.m_ranged ||
            !__instance.IsStaggering() || !Mastery.Remove(__instance.GetInstanceID())) return;
        Reward(Player.m_localPlayer, __instance);
    }

    private static void BeforeDeath(Character __instance, ref bool __state) =>
        __state = __instance != null && !__instance.IsDead() && __instance.GetHealth() <= 0f;

    private static void AfterDeath(Character __instance, bool __state)
    {
        if (!__state || __instance == null || __instance.IsPlayer()) return;
        var player = Player.m_localPlayer;
        if (player == null || ValheimLegends.ValheimLegends.vl_player?.vl_class !=
            ValheimLegends.ValheimLegends.PlayerClass.Duelist ||
            Vector3.Distance(player.transform.position, __instance.transform.position) > 70f) return;
        if (Death.Remove(__instance.GetInstanceID())) Reward(player, __instance);
        else if (Mastery.Remove(__instance.GetInstanceID()))
            player.GetSEMan().RemoveStatusEffect("SE_VL_Ability1_CD".GetStableHashCode(), false);
    }

    private static void Reward(Player player, Character target)
    {
        var baseReward = Mathf.Sqrt(target.GetMaxHealth());
        var amount = Mathf.CeilToInt(baseReward *
            (2f + LevelSystem.Instance.getAddCriticalChance() / 40f));
        if (!AwardCoins(player, amount)) return;
        player.GetSEMan().RemoveStatusEffect("SE_VL_Ability1_CD".GetStableHashCode(), false);
        player.Message(MessageHud.MessageType.TopLeft,
            $"Spoiled {amount} coins from {target.GetHoverName()}!");
    }

    internal static bool AwardCoins(Player player, int amount)
    {
        var prefab = ZNetScene.instance?.GetPrefab("Coins")?.GetComponent<ItemDrop>();
        if (prefab == null || amount <= 0) return false;
        if (player.GetInventory().CanAddItem(prefab.gameObject, amount))
            player.GetInventory().AddItem(prefab.gameObject, amount);
        else ItemDrop.DropItem(prefab.m_itemData, amount, player.transform.position, Quaternion.identity);
        return true;
    }
}
