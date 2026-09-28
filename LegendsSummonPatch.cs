using System;
using System.Reflection;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsSummonPatch
{
    private const string OwnerKey = "VL_SummonOwner";
    private const string DamageKey = "VL_SummonDamage";
    private static readonly FieldInfo CharacterField = AccessTools.Field(typeof(SEMan), "m_character");
    [ThreadStatic] private static Humanoid attackingWolf;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(SEMan), nameof(SEMan.AddStatusEffect),
                new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float) }),
            postfix: new HarmonyMethod(typeof(LegendsSummonPatch), nameof(Capture)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage)),
            prefix: new HarmonyMethod(typeof(LegendsSummonPatch), nameof(TagWolf)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage)),
            prefix: new HarmonyMethod(typeof(LegendsSummonPatch), nameof(Normalize)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(Attack), "Start"),
            prefix: new HarmonyMethod(typeof(LegendsSummonPatch), nameof(BeforeAttack)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(LegendsSummonPatch), nameof(AfterAttack)));
    }

    private static void BeforeAttack(Humanoid character) => attackingWolf =
        character != null && character.name.StartsWith("VL_ShadowWolf", StringComparison.Ordinal) ? character : null;

    private static void AfterAttack() => attackingWolf = null;

    private static void TagWolf(ref HitData hit)
    {
        if (hit != null && hit.GetAttacker() == null && attackingWolf != null) hit.SetAttacker(attackingWolf);
    }

    private static void Capture(SEMan __instance, StatusEffect __0, StatusEffect __result)
    {
        var owner = CharacterField.GetValue(__instance) as Character;
        if (owner == null || owner is Player) return;
        Player caster;
        Skills.SkillType school;
        float coefficient;
        float config;
        switch (__0)
        {
            case SE_RootsBuff roots:
                caster = roots.summoner;
                school = ValheimLegends.ValheimLegends.ConjurationSkill;
                coefficient = 0.40f;
                config = VL_GlobalConfigs.c_druidDefenders;
                break;
            case SE_Companion companion:
                caster = companion.summoner;
                if (owner.m_name == "Drusquito")
                {
                    school = ValheimLegends.ValheimLegends.ConjurationSkill;
                    coefficient = 0.22f;
                    config = VL_GlobalConfigs.c_druidDefenders;
                }
                else if (owner.name.StartsWith("VL_ShadowWolf", StringComparison.Ordinal))
                {
                    school = ValheimLegends.ValheimLegends.ConjurationSkill;
                    coefficient = 0.30f;
                    config = VL_GlobalConfigs.c_rangerShadowWolf;
                }
                else if (ValheimLegends.ValheimLegends.vl_player?.vl_class ==
                         ValheimLegends.ValheimLegends.PlayerClass.Metavoker)
                {
                    school = ValheimLegends.ValheimLegends.IllusionSkill;
                    coefficient = 0.25f;
                    config = VL_GlobalConfigs.c_metavokerReplica;
                }
                else return;
                break;
            default:
                return;
        }
        if (caster == null || caster != Player.m_localPlayer) return;
        var view = owner.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner()) return;
        var zdo = view.GetZDO();
        zdo.Set(OwnerKey, caster.GetZDOID());
        if (__0 is SE_Companion) zdo.Set("VL_Companion_Summoner", caster.GetZDOID());
        zdo.Set(DamageKey, LegendsEconomyPatch.Magic(caster.GetSkills().GetSkillLevel(school),
            LevelSystem.Instance.getParameter(Parameter.Body), coefficient, config));
        if (__0 is SE_RootsBuff rootsEffect) rootsEffect.damageModifier = 1f;
        if (__result is SE_RootsBuff appliedRoots) appliedRoots.damageModifier = 1f;
        if (__0 is SE_Companion companionEffect) companionEffect.damageModifier = 1f;
        if (__result is SE_Companion appliedCompanion) appliedCompanion.damageModifier = 1f;
    }

    private static void Normalize(ref HitData hit)
    {
        var attacker = hit.GetAttacker();
        if (attacker == null) return;
        var view = attacker.GetComponent<ZNetView>();
        if (view == null || !view.IsValid()) return;
        var zdo = view.GetZDO();
        if (zdo == null || zdo.GetZDOID(OwnerKey) == ZDOID.None) return;
        if (attacker.GetSEMan().GetStatusEffect("SE_VL_Companion".GetStableHashCode()) is SE_Companion companion)
            companion.damageModifier = 1f;
        if (attacker.GetSEMan().GetStatusEffect("SE_VL_RootsBuff".GetStableHashCode()) is SE_RootsBuff roots)
            roots.damageModifier = 1f;
        var expected = zdo.GetFloat(DamageKey, 0f);
        if (attacker.name.StartsWith("VL_ShadowWolf", StringComparison.Ordinal) &&
            ZNetScene.instance?.FindInstance(zdo.GetZDOID(OwnerKey))?.GetComponent<Player>() is Player caster &&
            caster == Player.m_localPlayer)
            expected = LegendsEconomyPatch.Magic(
                caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill),
                LevelSystem.Instance.getParameter(Parameter.Body), 0.30f, VL_GlobalConfigs.c_rangerShadowWolf);
        var total = hit.m_damage.GetTotalDamage();
        if (total > 0f && expected > 0f) hit.m_damage.Modify(expected / total);
    }
}
