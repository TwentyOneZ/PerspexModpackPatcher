using System;
using System.Collections.Generic;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsRangerPatch
{
    private struct InputState
    {
        internal GameObject Wolf;
        internal bool Summoning;
        internal bool ShadowPresent;
    }

    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Class_Ranger), nameof(Class_Ranger.Process_Input)),
        prefix: new HarmonyMethod(typeof(LegendsRangerPatch), nameof(BeforeInput)),
        postfix: new HarmonyMethod(typeof(LegendsRangerPatch), nameof(AfterInput)));

    private static bool BeforeInput(Player player, out InputState __state)
    {
        var effects = player.GetSEMan();
        __state = new InputState
        {
            Wolf = Class_Ranger.GO_Wolf,
            Summoning = VL_Utility.Ability2_Input_Down &&
                        !effects.HaveStatusEffect("SE_VL_Ability2_CD".GetStableHashCode()),
            ShadowPresent = effects.HaveStatusEffect("SE_VL_ShadowStalk".GetStableHashCode())
        };
        if (player == Player.m_localPlayer && VL_Utility.Ability3_Input_Down && player.IsSitting())
        {
            LegendsClassRepairPatch.Repair(player, LegendsClassRepairPatch.Kind.Ranger);
            return false;
        }
        if (player == Player.m_localPlayer && VL_Utility.Ability3_Input_Down && player.IsBlocking())
        {
            LegendsClassCraftPatch.Craft(player, "Wood", 8, "ArrowWood", 20,
                VL_Utility.GetPowerShotCost(player), 3, 0.5f,
                ValheimLegends.ValheimLegends.DisciplineSkill, 0f);
            return false;
        }
        if (VL_Utility.Ability3_Input_Down || !VL_Utility.Ability2_Input_Down || __state.Summoning)
            return true;
        if (player.IsBlocking()) Dismiss(player);
        else Heal(player);
        return false;
    }

    private static void AfterInput(Player player, InputState __state)
    {
        if (__state.Summoning && Class_Ranger.GO_Wolf != null &&
            Class_Ranger.GO_Wolf != __state.Wolf)
        {
            var wolf = Class_Ranger.GO_Wolf.GetComponent<Character>();
            if (wolf != null)
            {
                var attributes = LevelSystem.Instance;
                var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill);
                var level = skill * (1f + Mathf.Clamp(attributes.getAddStamina() / 200f +
                    attributes.getAddMagicDamage() / 80f, 0f, 0.5f));
                var health = 80f + 9f * level;
                wolf.SetMaxHealth(health);
                wolf.SetHealth(health);
                var view = wolf.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.IsOwner())
                {
                    view.GetZDO().Set("VL_SummonOwner", player.GetZDOID());
                    view.GetZDO().Set("VL_SummonDamage", LegendsEconomyPatch.Magic(skill,
                        attributes.getParameter(Parameter.Body), 0.30f, VL_GlobalConfigs.c_rangerShadowWolf));
                    view.GetZDO().Set("VL_Companion_Summoner", player.GetZDOID());
                    view.GetZDO().Set("VL_Companion_Scale", 0.5f + 0.015f * level);
                }
                if (wolf.GetSEMan().GetStatusEffect("SE_VL_Companion".GetStableHashCode()) is SE_Companion companion)
                    companion.damageModifier = 1f;
            }
        }
        if (VL_Utility.Ability1_Input_Down && !__state.ShadowPresent &&
            player.GetSEMan().GetStatusEffect("SE_VL_ShadowStalk".GetStableHashCode()) is SE_ShadowStalk shadow)
        {
            var attributes = LevelSystem.Instance;
            var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
            var level = skill * (1f + Mathf.Clamp(attributes.getAddPhysicDamage() / 40f +
                attributes.getAddAttackSpeed() / 40f, 0f, 0.5f));
            shadow.m_ttl = SE_ShadowStalk.m_baseTTL * (1f + 0.02f * level);
            shadow.speedAmount = 1.5f + 0.01f * level * VL_GlobalConfigs.c_rangerShadowStalk;
            shadow.speedDuration = 3f + 0.03f * level;
        }
    }

    private static List<Character> Wolves(Player player)
    {
        var nearby = new List<Character>();
        Character.GetCharactersInRange(player.transform.position, 25f, nearby);
        nearby.RemoveAll(character => character == null ||
            !character.gameObject.name.ToLowerInvariant().Contains("vl_shadowwolf") ||
            character.GetSEMan().GetStatusEffect("SE_VL_Companion".GetStableHashCode())
                is not SE_Companion companion ||
            companion.summoner != player &&
            character.GetComponent<ZNetView>()?.GetZDO()?.GetZDOID("VL_Companion_Summoner") !=
            player.GetZDOID());
        return nearby;
    }

    private static void Dismiss(Player player)
    {
        var wolves = Wolves(player);
        if (wolves.Count == 0)
        {
            player.Message(MessageHud.MessageType.TopLeft, "No Shadow Wolf to dismiss.");
            return;
        }
        var wolf = wolves[0];
        var cooldown = player.GetSEMan().GetStatusEffect("SE_VL_Ability2_CD".GetStableHashCode());
        if (cooldown != null)
        {
            var hp = Mathf.Max(0.01f, wolf.GetHealthPercentage());
            var remaining = Mathf.Min(cooldown.m_ttl, Mathf.Sqrt(cooldown.m_ttl / hp));
            player.GetSEMan().RemoveStatusEffect(cooldown, false);
            var revised = ScriptableObject.CreateInstance<SE_Ability2_CD>();
            revised.m_ttl = remaining;
            player.GetSEMan().AddStatusEffect(revised);
        }
        if (wolf.GetBaseAI() is MonsterAI ai) ai.SetFollowTarget(null);
        wolf.m_faction = Character.Faction.MountainMonsters;
        var hit = new HitData();
        hit.m_damage.m_slash = 9999f;
        wolf.Damage(hit);
    }

    private static void Heal(Player player)
    {
        var inventory = player.GetInventory();
        if (inventory == null) return;
        ItemDrop.ItemData food = null;
        var power = 0f;
        for (var row = 0; row < inventory.GetHeight() && food == null; row++)
            for (var column = 0; column < inventory.GetWidth() && food == null; column++)
            {
                var item = inventory.GetItemAt(column, row);
                var amount = item?.m_shared?.m_name switch
                {
                    "$item_necktail" => 0.10f,
                    "$item_boar_meat" => 0.15f,
                    "$item_deer_meat" or "$item_fish_raw" => 0.20f,
                    "$item_sausages" or "$item_loxmeat" => 0.25f,
                    _ => 0f
                };
                if (amount <= 0f) continue;
                food = item;
                power = amount;
            }
        if (food == null)
        {
            player.Message(MessageHud.MessageType.TopLeft, "Not enough wolf food in inventory to heal Wolf.");
            return;
        }
        var wolves = Wolves(player);
        var wolf = wolves.Find(candidate => candidate.GetHealthPercentage() < 1f);
        if (wolf == null)
        {
            player.Message(MessageHud.MessageType.TopLeft, "No wounded Shadow Wolf nearby.");
            return;
        }
        var stamina = VL_Utility.GetSummonWolfCost(player) * 2f * power;
        if (player.GetStamina() < stamina)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Not enough stamina to heal Wolf: ({player.GetStamina():0.#}/{stamina:0.#})");
            return;
        }
        player.UseStamina(stamina);
        inventory.RemoveOneItem(food);
        player.StartEmote("cheer");
        wolf.Heal(Mathf.Max(wolf.GetMaxHealth() * power, power * 10f));
        var attributes = LevelSystem.Instance;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill);
        var level = skill * (1f + Mathf.Clamp(attributes.getAddStamina() / 200f +
            attributes.getAddMagicDamage() / 80f, 0f, 0.5f));
        var heal = LegendsEconomyPatch.Healing(2f, skill) * VL_GlobalConfigs.c_druidRegen;
        wolf.GetSEMan().AddStatusEffect("SE_VL_Regeneration".GetStableHashCode(), true,
            Mathf.RoundToInt(SE_Regeneration.m_baseTTL * (1f + level / 300f) * 1000f), heal);
        foreach (var name in new[] { "vfx_Potion_stamina_medium", "vfx_WishbonePing" })
        {
            var fx = ZNetScene.instance?.GetPrefab(name);
            if (fx != null) UnityEngine.Object.Instantiate(fx, wolf.transform.position, Quaternion.identity);
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.ConjurationSkill,
            VL_Utility.GetSummonWolfSkillGain(player) * power);
        player.Message(MessageHud.MessageType.TopLeft,
            $"Consumed 1 {food.m_shared.m_name} to heal your companion.");
    }
}
