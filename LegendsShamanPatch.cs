using System.Collections.Generic;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsShamanPatch
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(Class_Shaman), "Process_Input"),
        prefix: new HarmonyMethod(typeof(LegendsShamanPatch), nameof(Input)));

    private static bool Input(Player player)
    {
        if (player == Player.m_localPlayer && player.IsBlocking() && VL_Utility.Ability2_Input_Down)
        {
            var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
            var boosted = skill * (1f + Mathf.Clamp(LevelSystem.Instance.getAddCriticalChance() / 40f +
                LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));
            LegendsClassCraftPatch.Craft(player, "Thunderstone", 1, "questitem_wraiths_breath", 1,
                VL_Utility.GetShellCost(player), 2, VL_Utility.GetHealCooldownTime * 20f / (1f + boosted / 150f),
                ValheimLegends.ValheimLegends.AlterationSkill, VL_Utility.GetHealSkillGain);
            return false;
        }
        return ChainHealing(player);
    }

    private static bool ChainHealing(Player player)
    {
        if (player != Player.m_localPlayer || !player.IsBlocking() || !VL_Utility.Ability3_Input_Down)
            return true;
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        if (player.GetSEMan().HaveStatusEffect("SE_VL_Ability3_CD".GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return false;
        }

        var attributes = LevelSystem.Instance;
        var stamina = 20f * VL_GlobalConfigs.g_EnergyCostModifer *
                      LegendsEconomyPatch.Reduction(attributes.getParameter(Parameter.Agility));
        if (player.GetStamina() < stamina)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {stamina:0.#} stamina for Chain Healing.");
            return false;
        }

        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        var radiusSkill = skill * (1f + Mathf.Clamp(
            attributes.getAddCriticalChance() / 40f + attributes.getAddMagicDamage() / 80f,
            0f, 0.5f));
        var radius = 20f + 0.2f * radiusSkill;
        var amount = LegendsEconomyPatch.Healing(12f, skill) * VL_GlobalConfigs.c_priestHeal;

        var effect = ScriptableObject.CreateInstance<SE_Ability3_CD>();
        effect.m_ttl = VL_Utility.GetSpiritBombCooldown(player);
        player.GetSEMan().AddStatusEffect(effect);
        player.UseStamina(stamina);
        player.StartEmote("challenge");

        var nearby = new List<Character>();
        Character.GetCharactersInRange(player.transform.position, radius, nearby);
        foreach (var target in nearby)
        {
            if (BaseAI.IsEnemy(target, player)) continue;
            target.Heal(amount);
            amount *= 0.7f;
        }

        foreach (var name in new[] { "fx_guardstone_permitted_add", "vfx_WishbonePing" })
        {
            var fx = ZNetScene.instance?.GetPrefab(name);
            if (fx != null) Object.Instantiate(fx,
                name == "fx_guardstone_permitted_add" ? player.GetCenterPoint() : player.transform.position,
                Quaternion.identity);
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.AlterationSkill, VL_Utility.GetSpiritBombSkillGain(player));
        return false;
    }
}
