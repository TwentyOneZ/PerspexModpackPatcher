using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMageFrostPatch
{
    private static bool blizzard;
    private static float chargeTimer;
    private static float shardTimer;
    private static int ticks;
    private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small",
        "piece_nonsolid", "terrain", "vehicle", "piece", "viewblock", "character", "character_net", "character_ghost");

    internal static void Cancel()
    {
        if (blizzard) End();
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsMageFrostPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsMageFrostPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsMageFrostPatch), nameof(OnDamage)));
        harmony.Patch(AccessTools.Method(typeof(Projectile), "OnHit"),
            prefix: new HarmonyMethod(typeof(LegendsMageFrostPatch), nameof(OnShardHit)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects != null &&
            !__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == "SE_VL_Frozen"))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexFrozen>());
    }

    internal static void Input(Player player)
    {
        var affinity = LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Frost);
        if (affinity == null) return;
        if (VL_Utility.Ability1_Input_Down && LegendsMageAffinityPatch.Ready("IceShard"))
        {
            if (affinity.Charges >= 1 && player.GetStamina() >= VL_Utility.GetFireballCost)
            {
                player.UseStamina(VL_Utility.GetFireballCost);
                affinity.Consume(1);
                LegendsMageAffinityPatch.Cooldown("IceShard", 0.5f);
                player.StartEmote("point");
                Shard(player, false);
                player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, VL_Utility.GetFireballSkillGain * 0.1f);
            }
        }
        if (VL_Utility.Ability2_Input_Down && LegendsMageAffinityPatch.Ready("FrostNova"))
        {
            if (affinity.Charges >= 3 && player.GetStamina() >= VL_Utility.GetFrostNovaCost)
            {
                player.UseStamina(VL_Utility.GetFrostNovaCost);
                affinity.Consume(3);
                LegendsMageAffinityPatch.Cooldown("FrostNova", VL_Utility.GetFrostNovaCooldownTime);
                Nova(player);
            }
        }
        Blizzard(player, affinity);
    }

    private static void Shard(Player player, bool falling)
    {
        var prefab = ZNetScene.instance?.GetPrefab("VL_FrostDagger") ?? ZNetScene.instance?.GetPrefab("ice_arrow");
        if (prefab == null) return;
        var look = player.GetLookDir();
        var origin = player.GetEyePoint() + look * 0.2f + player.transform.up * 0.1f;
        Vector3 spawn, direction;
        if (falling)
        {
            var target = Physics.Raycast(player.GetEyePoint(), look, out var ray, 30f, Mask) ?
                ray.point : player.GetEyePoint() + look * 30f;
            var circle = Random.insideUnitCircle * 6f;
            spawn = target + new Vector3(circle.x, 20f, circle.y);
            var drift = Random.insideUnitCircle * 0.4f;
            direction = new Vector3(drift.x, -1f, drift.y).normalized;
        }
        else
        {
            spawn = origin + player.transform.right * 0.28f;
            var target = Physics.Raycast(spawn, look, out var ray, 1000f, Mask) ?
                ray.point : spawn + look * 1000f;
            direction = (target - spawn).normalized;
        }
        var instance = Object.Instantiate(prefab, spawn, Quaternion.LookRotation(direction));
        var projectile = instance.GetComponent<Projectile>();
        if (projectile == null) return;
        projectile.name = falling ? "BlizzardShard" : "IceShard";
        projectile.m_respawnItemOnHit = false;
        projectile.m_ttl = falling ? 8f : 2f *
            LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Intellect));
        projectile.m_gravity = falling ? 0.1f : 0f;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill);
        var damage = LegendsEconomyPatch.Magic(skill,
            LevelSystem.Instance.getParameter(falling ? Parameter.Body : Parameter.Agility),
            falling ? 0.1f : 0.4f, VL_GlobalConfigs.c_mageFrostDagger);
        var hit = new HitData();
        hit.m_damage.m_frost = damage * 0.5f;
        hit.m_damage.m_pierce = damage * 0.5f;
        hit.m_toolTier = falling ? (short)137 : (short)138;
        hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
        hit.SetAttacker(player);
        LegendsMageArcanePatch.AddElemental(player, hit);
        projectile.Setup(player, direction * (falling ? 6f : 55f), -1f, hit, null, null);
        Traverse.Create(projectile).Field("m_skill").SetValue(ValheimLegends.ValheimLegends.EvocationSkill);
    }

    private static void Nova(Player player)
    {
        MageVisuals.Animate(player, "swing_axe1");
        MageVisuals.Spawn("fx_guardstone_activate", player.transform.position);
        player.GetSEMan().RemoveStatusEffect("Burning".GetStableHashCode(), false);
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill);
        var level = LegendsMageAffinityPatch.Evocation(player);
        foreach (var target in Character.GetAllCharacters())
        {
            if (target == null || !BaseAI.IsEnemy(player, target) ||
                Vector3.Distance(target.transform.position, player.transform.position) > 10f + level * 0.1f ||
                !VL_Utility.LOS_IsValid(target, player.GetCenterPoint(), player.transform.position + Vector3.up * 0.15f))
                continue;
            var hit = new HitData();
            hit.m_damage.m_frost = LegendsEconomyPatch.Magic(skill,
                LevelSystem.Instance.getParameter(Parameter.Vigour), 0.45f, VL_GlobalConfigs.c_mageFrostNova);
            hit.m_pushForce = 20f;
            hit.m_dir = target.transform.position - player.transform.position;
            hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
            hit.SetAttacker(player);
            LegendsMageArcanePatch.AddElemental(player, hit);
            target.Damage(hit);
            var effects = target.GetSEMan();
            effects.RemoveStatusEffect("SE_VL_Slow".GetStableHashCode(), false);
            var frozen = ScriptableObject.CreateInstance<PerspexFrozen>();
            frozen.m_ttl = 6f + 9f * level / 150f;
            effects.AddStatusEffect(frozen);
            MageVisuals.Spawn("fx_DvergerMage_Ice_hit", target.GetCenterPoint());
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, VL_Utility.GetFrostNovaSkillGain);
    }

    private static void Blizzard(Player player, PerspexMageAffinity affinity)
    {
        if (VL_Utility.Ability3_Input_Down && !blizzard)
        {
            if (!LegendsMageAffinityPatch.Ready("Blizzard") || affinity.Charges < 1) return;
            blizzard = true;
            chargeTimer = 1.1f;
            shardTimer = 0f;
            ticks = 0;
            ValheimLegends.ValheimLegends.isChanneling = true;
            ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
            MageVisuals.Animate(player, "gpower", 0.8f);
        }
        else if (blizzard && VL_Utility.Ability3_Input_Pressed)
        {
            shardTimer += Time.deltaTime;
            if (shardTimer >= 0.05f) { shardTimer = 0f; Shard(player, true); }
            chargeTimer += Time.deltaTime;
            if (chargeTimer >= 1f)
            {
                if (affinity.Charges < 1) { End(); return; }
                affinity.Consume(1);
                chargeTimer = 0f;
                ticks++;
                MageVisuals.Animate(player, "gpower", 0.8f);
            }
        }
        else if (blizzard && VL_Utility.Ability3_Input_Up) End();
    }

    private static void End()
    {
        blizzard = false;
        ValheimLegends.ValheimLegends.isChanneling = false;
        LegendsMageAffinityPatch.Cooldown("Blizzard", ticks * VL_Utility.GetMeteorCooldownTime);
    }

    private static void OnDamage(Character __instance, HitData hit)
    {
        if (hit == null || __instance == null || hit.m_damage.m_frost <= 0f) return;
        if (hit.m_toolTier == 137) { hit.m_toolTier = 0; return; }
        if (hit.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Mage ||
            LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Frost) is not { Focused: true, Charges: > 0 })
            return;
        var effects = __instance.GetSEMan();
        if (effects.HaveStatusEffect("SE_VL_FrostImmunity".GetStableHashCode())) return;
        var level = LegendsMageAffinityPatch.Evocation(player);
        var chance = (0.1f + level / 600f) * hit.m_damage.m_frost / Mathf.Max(1f, hit.GetTotalDamage());
        if (Random.value >= chance) return;
        var frozenId = "SE_VL_Frozen".GetStableHashCode();
        var slowId = "SE_VL_Slow".GetStableHashCode();
        var frozen = effects.GetStatusEffect(frozenId);
        var slow = effects.GetStatusEffect(slowId);
        if (frozen != null)
        {
            var ttl = Mathf.Min(frozen.m_ttl * 1.2f, 6f + 9f * level / 150f);
            effects.RemoveStatusEffect(frozenId, false);
            var refresh = ScriptableObject.CreateInstance<PerspexFrozen>();
            refresh.m_ttl = ttl;
            effects.AddStatusEffect(refresh);
            MageVisuals.Spawn("fx_DvergerMage_Ice_hit", hit.m_point);
        }
        else if (slow != null)
        {
            effects.RemoveStatusEffect(slowId, false);
            var freeze = ScriptableObject.CreateInstance<PerspexFrozen>();
            freeze.m_ttl = 6f + 9f * level / 150f;
            effects.AddStatusEffect(freeze);
            MageVisuals.Spawn("fx_DvergerMage_Ice_hit", hit.m_point);
        }
        else
        {
            var effect = ScriptableObject.CreateInstance<SE_Slow>();
            effect.m_ttl = 4f + 6f * level / 150f;
            effect.speedAmount = 0.7f - level / 250f;
            effects.AddStatusEffect(effect);
        }
        var immunity = ScriptableObject.CreateInstance<StatusEffect>();
        immunity.name = "SE_VL_FrostImmunity";
        immunity.m_ttl = 1f;
        effects.AddStatusEffect(immunity);
    }

    private static void OnShardHit(Projectile __instance, Collider collider, Vector3 hitPoint, Character ___m_owner)
    {
        if (__instance == null || ___m_owner is not Player player || player != Player.m_localPlayer) return;
        if (__instance.name == "BlizzardShard")
        {
            MageVisuals.Spawn("vfx_ice_hit", hitPoint);
            var targets = new System.Collections.Generic.List<Character>();
            Character.GetCharactersInRange(hitPoint, 6f, targets);
            foreach (var target in targets)
            {
                if (target == null || !BaseAI.IsEnemy(player, target) ||
                    target.GetSEMan().HaveStatusEffect("SE_VL_BlizzardImmunity".GetStableHashCode())) continue;
                var hit = ShardHit(__instance, player, target.GetCenterPoint());
                hit.m_toolTier = 137;
                ApplyBlizzardFrost(target, LegendsMageAffinityPatch.Evocation(player));
                target.Damage(hit);
                var immunity = ScriptableObject.CreateInstance<StatusEffect>();
                immunity.name = "SE_VL_BlizzardImmunity";
                immunity.m_ttl = 0.5f;
                target.GetSEMan().AddStatusEffect(immunity);
            }
            __instance.m_damage = new HitData.DamageTypes();
        }
        else if (__instance.name == "IceShard" && collider != null &&
                 collider.GetComponentInParent<Character>() is { } target && BaseAI.IsEnemy(player, target))
        {
            var hit = ShardHit(__instance, player, hitPoint);
            hit.m_toolTier = 136;
            if (target.GetSEMan().HaveStatusEffect("SE_VL_Frozen".GetStableHashCode()))
            {
                hit.ApplyModifier(3f);
                player.Message(MessageHud.MessageType.TopLeft, "Ice Shard Critical! (3.0x damage!)");
                MageVisuals.Spawn("vfx_ice_destroyed", hitPoint);
                MageVisuals.Spawn("sfx_ice_destroyed", hitPoint);
            }
            target.Damage(hit);
            __instance.m_damage = new HitData.DamageTypes();
        }
    }

    private static HitData ShardHit(Projectile projectile, Player player, Vector3 point)
    {
        var hit = new HitData();
        hit.m_damage = projectile.m_damage.Clone();
        hit.m_point = point;
        hit.m_dir = projectile.transform.forward;
        hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
        hit.SetAttacker(player);
        return hit;
    }

    private static void ApplyBlizzardFrost(Character target, float level)
    {
        var effects = target.GetSEMan();
        var frozen = effects.GetStatusEffect("SE_VL_Frozen".GetStableHashCode());
        var slow = effects.GetStatusEffect("SE_VL_Slow".GetStableHashCode());
        if (frozen != null)
        {
            var ttl = Mathf.Min(frozen.m_ttl * 1.2f, 6f + 9f * level / 150f);
            effects.RemoveStatusEffect(frozen, false);
            var effect = ScriptableObject.CreateInstance<PerspexFrozen>();
            effect.m_ttl = ttl;
            effects.AddStatusEffect(effect);
            MageVisuals.Spawn("fx_DvergerMage_Ice_hit", target.GetCenterPoint());
        }
        else if (slow != null)
        {
            effects.RemoveStatusEffect(slow, false);
            var effect = ScriptableObject.CreateInstance<PerspexFrozen>();
            effect.m_ttl = 6f + 9f * level / 150f;
            effects.AddStatusEffect(effect);
            MageVisuals.Spawn("fx_DvergerMage_Ice_hit", target.GetCenterPoint());
        }
        else
        {
            var effect = ScriptableObject.CreateInstance<SE_Slow>();
            effect.m_ttl = 4f + 6f * level / 150f;
            effect.speedAmount = 0.7f - level / 250f;
            effects.AddStatusEffect(effect);
        }
    }
}

internal sealed class PerspexFrozen : StatusEffect
{
    public PerspexFrozen()
    {
        name = "SE_VL_Frozen";
        m_name = "Frozen";
        m_tooltip = "Frozen solid. Unable to move.";
        m_ttl = 10f;
        m_startMessage = "Frozen";
        m_stopMessage = "Thawed";
        m_icon = MageVisuals.Icon("FreezeGland");
    }

    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        speed = 0f;
        base.ModifySpeed(baseSpeed, ref speed, character, dir);
    }

    public override void Setup(Character character)
    {
        base.Setup(character);
        if (m_icon == null) m_icon = MageVisuals.Icon("FreezeGland");
        var prefab = ZNetScene.instance?.GetPrefab("vfx_Freezing");
        if (prefab != null && character != null)
        {
            var effect = Object.Instantiate(prefab, character.transform);
            effect.transform.localPosition = Vector3.up;
        }
    }
}
