using System;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace PerspexModpackPatcher;

internal static class QuiverAmmoRequirementsPatch
{
    private const string Owner = "WackyMole.ItemRequiresSkillLevel";
    private static readonly System.Reflection.FieldInfo AmmoField = AccessTools.Field(typeof(Humanoid), "m_ammoItem");
    private static readonly Type RequirementsType = AccessTools.TypeByName("ItemRequiresSkillLevel.ItemRequiresSkillLevel");
    private static readonly System.Reflection.FieldInfo MessageField = AccessTools.Field(RequirementsType, "cantUseAmmomessage");
    private static readonly System.Reflection.FieldInfo ShowMessagesField = AccessTools.Field(RequirementsType, "ShowBlockMessages");

    internal static void Install(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(Attack), nameof(Attack.StartDraw));
        var original = Harmony.GetPatchInfo(target)?.Prefixes
            .FirstOrDefault(patch => patch.owner == Owner && patch.PatchMethod.Name == "StartDraw");
        if (original == null)
            throw new InvalidOperationException("ItemRequiresSkillLevel StartDraw prefix was not found.");

        harmony.Unpatch(target, original.PatchMethod);
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(QuiverAmmoRequirementsPatch), nameof(StartDraw)));
    }

    private static bool StartDraw(Humanoid character, ItemDrop.ItemData weapon)
    {
        if (character is not Player || string.IsNullOrEmpty(weapon?.m_shared?.m_ammoType))
            return true;

        var ammoType = weapon.m_shared.m_ammoType;
        var selected = character.GetAmmoItem();
        if (Usable(selected, ammoType))
            return true;

        foreach (var item in character.GetInventory().GetAllItems())
        {
            if (!Usable(item, ammoType)) continue;
            AmmoField.SetValue(character, item);
            return true;
        }

        if (MessageHud.instance != null &&
            (ShowMessagesField?.GetValue(null) as ConfigEntry<bool>)?.Value == true)
            character.Message(MessageHud.MessageType.Center,
                (MessageField?.GetValue(null) as ConfigEntry<string>)?.Value ?? "You Can't Use This Ammo");
        return false;
    }

    private static bool Usable(ItemDrop.ItemData item, string ammoType) =>
        item != null && item.m_shared.m_ammoType == ammoType &&
        (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo ||
         item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable) &&
        item.IsEquipable();
}
