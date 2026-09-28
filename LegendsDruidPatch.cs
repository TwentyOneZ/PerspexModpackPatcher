using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDruidPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Druid), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsDruidPatch), nameof(FirstAbility)),
            transpiler: new HarmonyMethod(typeof(LegendsDruidPatch), nameof(RootCalls)));
        harmony.Patch(AccessTools.Method(typeof(StatusEffect), nameof(StatusEffect.SetLevel)),
            postfix: new HarmonyMethod(typeof(LegendsDruidPatch), nameof(RegenerationLevel)));
        harmony.Patch(AccessTools.Method(typeof(SE_Regeneration), nameof(SE_Regeneration.UpdateStatusEffect)),
            prefix: new HarmonyMethod(typeof(LegendsDruidPatch), nameof(RegenerationTick)));
    }

    private static IEnumerable<CodeInstruction> RootCalls(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Projectile) &&
                method.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsDruidPatch), nameof(RootSetup));
                count++;
            }
            yield return instruction;
        }
        if (count != 2) throw new System.InvalidOperationException($"Unexpected Druid root sites: {count}");
    }

    private static void RootSetup(Projectile projectile, Character owner, Vector3 velocity,
        float noise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill);
        hit.m_damage.m_pierce = LegendsEconomyPatch.Magic(school,
            LevelSystem.Instance.getParameter(Parameter.Body), 0.35f, VL_GlobalConfigs.c_druidVines);
        hit.m_skill = ValheimLegends.ValheimLegends.ConjurationSkill;
        hit.SetAttacker(caster);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void RegenerationLevel(StatusEffect __instance, int itemLevel, float skillLevel)
    {
        if (__instance is not SE_Regeneration regeneration || (itemLevel != 1 && itemLevel < 1000)) return;
        regeneration.m_HealAmount = skillLevel;
        regeneration.m_ttl = itemLevel >= 1000 ? itemLevel / 1000f : SE_Regeneration.m_baseTTL;
        regeneration.doOnce = false;
    }

    private static void RegenerationTick(SE_Regeneration __instance) => __instance.doOnce = false;

    private static bool FirstAbility(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability1_Input_Down) return true;
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect("SE_VL_Ability1_CD".GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return false;
        }
        var cost = VL_Utility.GetRegenerationCost;
        if (player.GetStamina() < cost)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {cost:0.#} stamina.");
            return false;
        }
        if (player.IsBlocking())
        {
            LegendsClassCraftPatch.Craft(player, "AncientSeed", 1, "questitem_wraiths_breath", 1,
                cost, 1, VL_Utility.GetHealCooldownTime * 20f,
                ValheimLegends.ValheimLegends.AlterationSkill, VL_Utility.GetRegenerationSkillGain);
            return false;
        }

        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        var boosted = skill * (1f + Mathf.Clamp(LevelSystem.Instance.getAddCriticalChance() / 40f +
                                                 LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));
        var targets = new List<Character>();
        Character.GetCharactersInRange(player.GetCenterPoint(), 30f + 0.2f * boosted, targets);
        var heal = LegendsEconomyPatch.Healing(2f, skill) * VL_GlobalConfigs.c_druidRegen;
        var cooldown = ScriptableObject.CreateInstance<SE_Ability1_CD>();
        cooldown.m_ttl = VL_Utility.GetRegenerationCooldownTime;
        effects.AddStatusEffect(cooldown);
        player.UseStamina(cost);
        player.StartEmote("cheer");
        Effects(player);
        foreach (var target in targets)
            if (!BaseAI.IsEnemy(player, target))
                target.GetSEMan().AddStatusEffect("SE_VL_Regeneration".GetStableHashCode(), true, 1, heal);
        player.RaiseSkill(ValheimLegends.ValheimLegends.AlterationSkill, VL_Utility.GetRegenerationSkillGain);
        return false;
    }

    private static void Effects(Player player)
    {
        foreach (var name in new[] { "fx_guardstone_permitted_add", "vfx_WishbonePing" })
        {
            var fx = ZNetScene.instance?.GetPrefab(name);
            if (fx != null) Object.Instantiate(fx, player.transform.position, Quaternion.identity);
        }
    }
}
