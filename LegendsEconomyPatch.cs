using System;
using System.Collections.Generic;
using System.Reflection;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

// Replaces the original ability getter values without shipping a modified Legends assembly.
internal static class LegendsEconomyPatch
{
    private static readonly Dictionary<string, float> Costs = new()
    {
        ["ZoneCharge"] = 15, ["Weaken"] = 15, ["Charm"] = 20, ["FlyingKick"] = 20,
        ["PoisonBomb"] = 40, ["Backstab"] = 25, ["Fade"] = 10, ["Sanctify"] = 20,
        ["Heal"] = 25, ["Purge"] = 20, ["QuickShot"] = 15, ["Riposte"] = 15,
        ["BlinkStrike"] = 20, ["Light"] = 15, ["Warp"] = 15, ["Replica"] = 25,
        ["ForceWave"] = 15, ["Fireball"] = 10, ["Meteor"] = 15, ["FrostNova"] = 10,
        ["Bulwark"] = 20, ["Leap"] = 25, ["Stagger"] = 15, ["VineHook"] = 15,
        ["Regeneration"] = 15, ["Root"] = 20, ["Defender"] = 30, ["ChainHealing"] = 20
    };

    private static readonly Dictionary<string, float> Cooldowns = new()
    {
        ["ZoneCharge"] = 600, ["Weaken"] = 60, ["Charm"] = 60, ["MeteorPunch"] = 1,
        ["PsiBolt"] = 1, ["FlyingKick"] = 6, ["PoisonBomb"] = 30, ["Backstab"] = 20,
        ["Fade"] = 15, ["Sanctify"] = 45, ["Heal"] = 30, ["Purge"] = 15,
        ["QuickShot"] = 10, ["Riposte"] = 6, ["BlinkStrike"] = 30, ["Light"] = 20,
        ["Warp"] = 6, ["Replica"] = 30, ["ForceWave"] = 20, ["Fireball"] = 2,
        ["Meteor"] = 30, ["FrostNova"] = 20, ["Bulwark"] = 60, ["Leap"] = 15,
        ["Stagger"] = 20, ["HarpoonPull"] = 10, ["Regeneration"] = 60,
        ["Root"] = 20, ["Defender"] = 120
    };

    private static readonly Dictionary<string, float> SkillGains = new()
    {
        ["ZoneCharge"] = 8f, ["Weaken"] = 1.4f, ["Charm"] = 2.6f,
        ["MeteorPunch"] = 4f, ["PsiBolt"] = 5f, ["FlyingKick"] = 0.8f,
        ["PoisonBomb"] = 1.8f, ["Backstab"] = 2.6f, ["Fade"] = 1f,
        ["Sanctify"] = 1.8f, ["Heal"] = 1.3f, ["Purge"] = 0.8f,
        ["QuickShot"] = 0.5f, ["Riposte"] = 0.2f, ["BlinkStrike"] = 1.5f,
        ["Light"] = 1f, ["Warp"] = 0.2f, ["Replica"] = 1.5f,
        ["ForceWave"] = 1.5f, ["Fireball"] = 1f, ["Meteor"] = 2f,
        ["FrostNova"] = 1f, ["Bulwark"] = 1.8f, ["Leap"] = 1.8f,
        ["Stagger"] = 1.8f, ["HarpoonPull"] = 1.8f, ["ShieldRelease"] = 1.8f,
        ["Regeneration"] = 2.7f, ["Root"] = 1.4f, ["Defender"] = 2.7f
    };

    // These nine abilities use Player-argument methods rather than the property getters above.
    private static readonly Dictionary<string, (float cost, float cooldown, float gain)> MethodAbilities = new()
    {
        ["Enrage"] = (25f, 60f, 1.8f),
        ["SpiritBomb"] = (15f, 30f, 1.8f),
        ["Shell"] = (25f, 120f, 1.8f),
        ["Dash"] = (25f, 10f, 0.5f),
        ["Berserk"] = (0f, 60f, 2.7f),
        ["Execute"] = (20f, 60f, 2.4f),
        ["PowerShot"] = (10f, 60f, 1.5f),
        ["ShadowStalk"] = (15f, 45f, 3f),
        ["SummonWolf"] = (30f, 600f, 27f)
    };

    internal static void Install(Harmony harmony)
    {
        var warpDrain = AccessTools.PropertyGetter(typeof(VL_Utility), "GetWarpCostPerUpdate");
        if (warpDrain == null) throw new MissingMethodException("Dekas Warp drain getter missing");
        harmony.Patch(warpDrain, prefix: new HarmonyMethod(typeof(LegendsEconomyPatch), nameof(WarpDrain)));
        foreach (var pair in Costs)
        {
            var getter = AccessTools.PropertyGetter(typeof(VL_Utility), "Get" + pair.Key + "Cost");
            if (getter != null) harmony.Patch(getter, prefix: new HarmonyMethod(typeof(LegendsEconomyPatch), nameof(Cost)));
        }
        foreach (var pair in Cooldowns)
        {
            var getter = AccessTools.PropertyGetter(typeof(VL_Utility), "Get" + pair.Key + "CooldownTime") ??
                         AccessTools.PropertyGetter(typeof(VL_Utility), "Get" + pair.Key + "Cooldown");
            if (getter != null) harmony.Patch(getter, prefix: new HarmonyMethod(typeof(LegendsEconomyPatch), nameof(Cooldown)));
        }
        foreach (var ability in SkillGains.Keys)
        {
            var getter = AccessTools.PropertyGetter(typeof(VL_Utility), "Get" + ability + "SkillGain");
            if (getter != null) harmony.Patch(getter, prefix: new HarmonyMethod(typeof(LegendsEconomyPatch), nameof(SkillGain)));
        }
        foreach (var ability in MethodAbilities.Keys)
            foreach (var kind in new[] { "Cost", "Cooldown", "SkillGain" })
            {
                var method = AccessTools.Method(typeof(VL_Utility), "Get" + ability + kind, new[] { typeof(Player) });
                if (method != null) harmony.Patch(method, prefix: new HarmonyMethod(typeof(LegendsEconomyPatch), nameof(AbilityMethod)));
            }
    }

    private static bool WarpDrain(ref float __result)
    {
        __result = VL_Utility.GetWarpCost * Time.deltaTime;
        return false;
    }

    private static bool Cost(MethodBase __originalMethod, ref float __result)
    {
        var name = __originalMethod.Name.Substring(7);
        var ability = name.Substring(0, name.Length - 4);
        __result = Costs[ability] * VL_GlobalConfigs.g_EnergyCostModifer *
            Reduction(LevelSystem.Instance.getParameter(Parameter.Agility));
        return false;
    }

    private static bool Cooldown(MethodBase __originalMethod, ref float __result)
    {
        var name = __originalMethod.Name.Substring(7);
        var suffix = name.EndsWith("CooldownTime", StringComparison.Ordinal) ? "CooldownTime" : "Cooldown";
        var ability = name.Substring(0, name.Length - suffix.Length);
        __result = Cooldowns[ability] * VL_GlobalConfigs.g_CooldownModifer *
            Reduction(LevelSystem.Instance.getParameter(Parameter.Intellect));
        return false;
    }

    private static bool AbilityMethod(MethodBase __originalMethod, ref float __result)
    {
        var name = __originalMethod.Name.Substring(3);
        var kind = name.EndsWith("Cooldown", StringComparison.Ordinal) ? "Cooldown" :
                   name.EndsWith("SkillGain", StringComparison.Ordinal) ? "SkillGain" : "Cost";
        var ability = name.Substring(0, name.Length - kind.Length);
        var values = MethodAbilities[ability];
        var level = LevelSystem.Instance;
        __result = kind == "Cooldown"
            ? values.cooldown * VL_GlobalConfigs.g_CooldownModifer * Reduction(level.getParameter(Parameter.Intellect))
            : kind == "SkillGain"
                ? values.gain * VL_GlobalConfigs.g_SkillGainModifer * (1f + level.getAddMagicDamage() / 16f)
                : values.cost * VL_GlobalConfigs.g_EnergyCostModifer * Reduction(level.getParameter(Parameter.Agility));
        return false;
    }

    private static bool SkillGain(MethodBase __originalMethod, ref float __result)
    {
        var name = __originalMethod.Name.Substring(7);
        var ability = name.Substring(0, name.Length - "SkillGain".Length);
        __result = SkillGains[ability] * VL_GlobalConfigs.g_SkillGainModifer *
                   (1f + LevelSystem.Instance.getAddMagicDamage() / 16f);
        return false;
    }

    internal static float Reduction(float attribute) => 1f - 0.005f * Math.Max(0f, Math.Min(100f, attribute));

    internal static float Healing(float baseAmount, float skill)
    {
        var level = LevelSystem.Instance;
        return baseAmount * (float)Math.Sqrt(4f + 0.9f * level.getLevel()) *
               (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, level.getParameter(Parameter.Body)))) *
               (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, skill))) *
               VL_GlobalConfigs.g_DamageModifer;
    }

    internal static float Magic(float school, float secondary, float coefficient, float config)
    {
        var level = LevelSystem.Instance;
        return (4f + 0.9f * level.getLevel()) * (1f + level.getAddMagicDamage() / 100f) *
               (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, school))) *
               (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, secondary))) *
               coefficient * VL_GlobalConfigs.g_DamageModifer * config;
    }

    internal static float Magic(float school, float coefficient, float config)
    {
        var level = LevelSystem.Instance;
        return (4f + 0.9f * level.getLevel()) * (1f + level.getAddMagicDamage() / 100f) *
               (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, school))) *
               coefficient * VL_GlobalConfigs.g_DamageModifer * config;
    }

    internal static HitData.DamageTypes Physical(Player player, float school, float secondary,
        float coefficient, float config)
    {
        var weapon = player.GetCurrentWeapon();
        var damage = weapon.GetDamage();
        if (!player.GetInventory().ContainsItem(weapon))
            damage.Modify(1f + LevelSystem.Instance.getAddPhysicDamage() / 100f);
        damage.Modify((0.75f + 0.005f * Math.Max(0f, Math.Min(100f, school))) *
                      (0.75f + 0.005f * Math.Max(0f, Math.Min(100f, secondary))) *
                      coefficient * VL_GlobalConfigs.g_DamageModifer * config);
        return damage;
    }
}
