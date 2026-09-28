using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMageFirePatch
{
    private static bool charging;
    private static int meteors;
    private static float timer;
    private static float skillGain;
    private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small",
        "piece_nonsolid", "terrain", "vehicle", "piece", "viewblock", "character", "character_net");

    internal static void Reset() { charging = false; meteors = 0; timer = 0f; skillGain = 0f; }

    internal static void Cancel()
    {
        if (charging) ValheimLegends.ValheimLegends.isChanneling = false;
        Reset();
    }

    internal static void Input(Player player)
    {
        var affinity = LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Fire);
        if (affinity == null) return;
        if (VL_Utility.Ability1_Input_Down && LegendsMageAffinityPatch.Ready("Fireball") &&
            affinity.Charges >= 1 && player.GetStamina() >= VL_Utility.GetFireballCost)
        {
            affinity.Consume(1);
            player.UseStamina(VL_Utility.GetFireballCost);
            LegendsMageAffinityPatch.Cooldown("Fireball", VL_Utility.GetFireballCooldownTime);
            player.StartEmote("cheer");
            MageVisuals.Spawn("fx_VL_Flames", player.transform.position);
            Fireball(player);
            player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, VL_Utility.GetFireballSkillGain);
        }
        if (VL_Utility.Ability2_Input_Down && LegendsMageAffinityPatch.Ready("FlameNova") &&
            affinity.Charges >= 3 && player.GetStamina() >= VL_Utility.GetFrostNovaCost)
        {
            affinity.Consume(3);
            player.UseStamina(VL_Utility.GetFrostNovaCost);
            LegendsMageAffinityPatch.Cooldown("FlameNova", VL_Utility.GetFrostNovaCooldownTime * 2f);
            Class_Mage.QueuedAttack = Class_Mage.MageAttackType.FlameNova;
            ValheimLegends.ValheimLegends.isChargingDash = true;
            ValheimLegends.ValheimLegends.dashCounter = 0;
            Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>()?.SetTrigger("swing_sledge");
        }
        MeteorInput(player, affinity);
    }

    private static float Damage(Player player, float coefficient, float config) =>
        LegendsEconomyPatch.Magic(player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill),
            coefficient, config);

    private static void Fireball(Player player)
    {
        var prefab = ZNetScene.instance?.GetPrefab("Imp_fireball_projectile");
        if (prefab == null) return;
        var origin = player.transform.position + player.transform.up * 2.5f + player.GetLookDir() * 0.5f;
        var target = Physics.Raycast(origin, player.GetLookDir(), out var ray, 1000f, Mask)
            ? ray.point : origin + player.GetLookDir() * 1000f;
        var velocity = (target - origin).normalized * 25f;
        var go = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.LookRotation(velocity));
        var projectile = go.GetComponent<Projectile>();
        if (projectile == null) return;
        projectile.name = "Fireball";
        projectile.m_respawnItemOnHit = false;
        projectile.m_ttl = 60f;
        projectile.m_gravity = 2.5f;
        projectile.m_rayRadius = 0.1f;
        projectile.m_aoe = 3f + 0.03f * LegendsMageAffinityPatch.Evocation(player);
        var hit = new HitData();
        var damage = Damage(player, 0.8f, VL_GlobalConfigs.c_mageFireball);
        hit.m_damage.m_fire = damage * 0.5f;
        hit.m_damage.m_blunt = damage * 0.5f;
        hit.m_pushForce = 2f;
        hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
        hit.SetAttacker(player);
        LegendsMageArcanePatch.AddElemental(player, hit);
        projectile.Setup(player, velocity, -1f, hit, null, null);
    }

    private static void MeteorInput(Player player, PerspexMageAffinity affinity)
    {
        if (VL_Utility.Ability3_Input_Down && !charging)
        {
            if (!LegendsMageAffinityPatch.Ready("Meteor") || affinity.Charges < 1 ||
                player.GetStamina() < VL_Utility.GetMeteorCost) return;
            player.UseStamina(VL_Utility.GetMeteorCost);
            ValheimLegends.ValheimLegends.isChanneling = true;
            ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
            charging = true;
            meteors = 0;
            timer = 0f;
            skillGain = 0f;
            MageVisuals.Animate(player, "gpower", 0.5f);
        }
        else if (charging && VL_Utility.Ability3_Input_Pressed)
        {
            var next = meteors + 1;
            if (player.GetStamina() <= 1f || affinity.Charges < next) { Finish(player); return; }
            player.UseStamina(VL_Utility.GetMeteorCost * Time.deltaTime);
            timer += Time.deltaTime;
            var interval = Mathf.Max(3f, 8f - LegendsMageAffinityPatch.Evocation(player) / 30f);
            if (timer < interval) return;
            affinity.Consume(next);
            meteors++;
            timer = 0f;
            skillGain += VL_Utility.GetMeteorSkillGain + (meteors > 1 ? 0.2f : 0f);
            var fx = ZNetScene.instance?.GetPrefab("fx_VL_Flames");
            if (fx != null) UnityEngine.Object.Instantiate(fx, player.transform.position, Quaternion.identity);
            MageVisuals.Animate(player, "gpower", 0.5f);
        }
        else if (charging && VL_Utility.Ability3_Input_Up) Finish(player);
    }

    private static void Finish(Player player)
    {
        if (meteors > 0)
        {
            CastMeteors(player);
            LegendsMageAffinityPatch.Cooldown("Meteor", meteors * VL_Utility.GetMeteorCooldownTime);
            player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, skillGain);
        }
        Reset();
        ValheimLegends.ValheimLegends.isChanneling = false;
    }

    private static void CastMeteors(Player player)
    {
        var prefab = ZNetScene.instance?.GetPrefab("projectile_meteor");
        if (prefab == null) return;
        var aim = Physics.Raycast(player.GetEyePoint(), player.GetLookDir(), out var ray, 1000f, Mask)
            ? ray.point : player.GetEyePoint() + player.GetLookDir() * 10f;
        var damage = Damage(player, 2f, VL_GlobalConfigs.c_mageMeteor);
        for (var i = 0; i < meteors; i++)
        {
            var origin = aim + new Vector3(UnityEngine.Random.Range(-8f, 8f), 100f,
                UnityEngine.Random.Range(-8f, 8f));
            var target = aim + new Vector3(UnityEngine.Random.Range(-8f, 8f), 0f,
                UnityEngine.Random.Range(-8f, 8f));
            var direction = (target - origin).normalized;
            var go = UnityEngine.Object.Instantiate(prefab, origin, Quaternion.LookRotation(direction));
            var projectile = go.GetComponent<Projectile>();
            if (projectile == null) continue;
            projectile.name = "Meteor" + i;
            projectile.m_respawnItemOnHit = false;
            projectile.m_rayRadius = 0.1f;
            projectile.m_aoe = 8f + 0.03f * LegendsMageAffinityPatch.Evocation(player);
            var hit = new HitData();
            hit.m_damage.m_fire = damage * 0.5f;
            hit.m_damage.m_blunt = damage * 0.5f;
            hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
            hit.SetAttacker(player);
            LegendsMageArcanePatch.AddElemental(player, hit);
            projectile.Setup(player, direction * 50f, -1f, hit, null, null);
        }
    }
}
