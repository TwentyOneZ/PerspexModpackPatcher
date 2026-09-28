using System;
using System.Linq;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsClassCraftPatch
{
    internal static void Craft(Player player, string material, int materialCount, string product,
        int productCount, float stamina, int slot, float cooldown, Skills.SkillType skill, float skillGain)
    {
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect(("SE_VL_Ability" + slot + "_CD").GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return;
        }
        var inventory = player.GetInventory();
        var materialPrefab = ZNetScene.instance?.GetPrefab(material)?.GetComponent<ItemDrop>();
        var productPrefab = ZNetScene.instance?.GetPrefab(product)?.GetComponent<ItemDrop>();
        if (inventory == null || materialPrefab == null || productPrefab == null)
        {
            player.Message(MessageHud.MessageType.Center, "Crafting item is unavailable.");
            return;
        }
        var stacks = inventory.GetAllItems().Where(item => item != null &&
            (item.m_dropPrefab?.name == material || item.m_shared.m_name == materialPrefab.m_itemData.m_shared.m_name)).ToArray();
        if (stacks.Sum(item => item.m_stack) < materialCount)
        {
            player.Message(MessageHud.MessageType.Center, $"Need {materialCount} {material}.");
            return;
        }
        if (player.GetStamina() < stamina)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {stamina:0.#} stamina.");
            return;
        }
        StatusEffect effect = slot switch
        {
            1 => ScriptableObject.CreateInstance<SE_Ability1_CD>(),
            2 => ScriptableObject.CreateInstance<SE_Ability2_CD>(),
            _ => ScriptableObject.CreateInstance<SE_Ability3_CD>()
        };
        var item = productPrefab.m_itemData.Clone();
        item.m_stack = productCount;
        var remaining = materialCount;
        foreach (var stack in stacks)
        {
            var count = Math.Min(stack.m_stack, remaining);
            inventory.RemoveItem(stack, count);
            remaining -= count;
            if (remaining == 0) break;
        }
        player.UseStamina(stamina);
        effect.m_ttl = cooldown;
        effects.AddStatusEffect(effect);
        if (!inventory.AddItem(item))
        {
            ItemDrop.DropItem(item, productCount, player.transform.position + player.transform.forward, Quaternion.identity);
            player.Message(MessageHud.MessageType.TopLeft, "Inventory full; crafted item dropped nearby.");
        }
        else player.Message(MessageHud.MessageType.TopLeft, $"Crafted {productCount} {item.m_shared.m_name}.");
        if (skillGain > 0f) player.RaiseSkill(skill, skillGain);
        player.StartEmote("cheer");
        MageVisuals.Spawn("vfx_Potion_stamina_medium", player.transform.position);
        MageVisuals.Spawn("vfx_WishbonePing", player.transform.position);
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
    }
}
