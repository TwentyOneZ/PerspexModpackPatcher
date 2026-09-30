using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMageArcanePatch
{
    internal enum Buff { Elemental, Intellect, Shield }
    private static readonly string[] Names =
    {
        "SE_VL_ElementalMastery", "SE_VL_ArcaneIntellect", "SE_VL_ManaShield"
    };
    private static bool redirecting;
    private static float lastShieldFx = -1f;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.GetMaxEitr)),
            postfix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(MaxEitr)));
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.HaveEitr)),
            postfix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(HaveEitr)));
        harmony.Patch(AccessTools.Method(typeof(Player), nameof(Player.UseEitr), new[] { typeof(float) }),
            prefix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(UseEitr)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(ShieldDamage)) { priority = Priority.First });
        var block = AccessTools.Method(typeof(Humanoid), "BlockAttack", new[] { typeof(HitData), typeof(Character) });
        if (block != null) harmony.Patch(block,
            postfix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(ShieldParry)));
        harmony.Patch(AccessTools.Method(typeof(Character), "CheckDeath"),
            prefix: new HarmonyMethod(typeof(LegendsMageArcanePatch), nameof(ShieldDeath)) { priority = Priority.First });
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        foreach (Buff buff in Enum.GetValues(typeof(Buff)))
            if (!__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == Names[(int)buff]))
                __instance.m_StatusEffects.Add(New(buff));
    }

    private static PerspexArcaneBuff New(Buff buff)
    {
        var effect = ScriptableObject.CreateInstance<PerspexArcaneBuff>();
        effect.Kind = buff;
        effect.name = Names[(int)buff];
        effect.m_name = buff == Buff.Shield ? "Eitr Shield" : buff == Buff.Intellect ? "Arcane Intellect" : "Elemental Mastery";
        effect.m_tooltip = buff == Buff.Shield ? "Absorbs damage using Eitr and Arcane charges." :
            buff == Buff.Intellect ? "Eitr costs stamina first. Consumes an Arcane charge every 20 seconds." :
            "Spells gain a portion of equipped weapon elemental damage. Consumes an Arcane charge every 15 seconds.";
        effect.m_icon = MageVisuals.Icon(buff == Buff.Shield ? "StaminaUpgrade_Greydwarf" :
            buff == Buff.Intellect ? "HelmetPointyHat" : "Eitr");
        return effect;
    }

    private static bool Has(Player player, Buff buff) => player != null &&
        player.GetSEMan()?.HaveStatusEffect(Names[(int)buff].GetStableHashCode()) == true;

    internal static void Input(Player player)
    {
        if (VL_Utility.Ability1_Input_Down) Toggle(player, Buff.Elemental);
        if (VL_Utility.Ability2_Input_Down) Toggle(player, Buff.Intellect);
        if (VL_Utility.Ability3_Input_Down) Toggle(player, Buff.Shield);
    }

    private static void Toggle(Player player, Buff buff)
    {
        var id = Names[(int)buff];
        if (!LegendsMageAffinityPatch.Ready(id)) return;
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect(id.GetStableHashCode()))
        {
            effects.RemoveStatusEffect(id.GetStableHashCode(), false);
            Fade(player);
            player.Message(MessageHud.MessageType.TopLeft, id.Substring(6) + ": OFF");
        }
        else
        {
            var affinity = LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Arcane);
            if (affinity == null || affinity.Charges < 1)
            {
                player.Message(MessageHud.MessageType.TopLeft, "Need 1 Arcane Charge.");
                return;
            }
            affinity.Consume(1);
            if (ObjectDB.instance != null) Register(ObjectDB.instance);
            effects.AddStatusEffect(New(buff));
            MageVisuals.Spawn(buff == Buff.Shield ? "fx_shield_start" :
                buff == Buff.Intellect ? "fx_guardstone_permitted_removed" : "fx_guardstone_activate",
                player.GetCenterPoint());
            MageVisuals.Animate(player, "gpower", 5f);
            player.Message(MessageHud.MessageType.TopLeft, id.Substring(6) + ": ON");
        }
        LegendsMageAffinityPatch.Cooldown(id, 1f);
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
    }

    private static void Fade(Player player)
    {
        MageVisuals.Spawn("vfx_HitSparks", player.GetEyePoint(), Quaternion.LookRotation(Vector3.up));
        MageVisuals.Spawn("sfx_lootspawn", player.GetEyePoint(), Quaternion.LookRotation(Vector3.up));
    }

    internal static void AddElemental(Player player, HitData hit)
    {
        if (!Has(player, Buff.Elemental) || player.GetCurrentWeapon() == null) return;
        var weapon = LegendsEconomyPatch.WeaponDamage(player);
        hit.m_damage.m_fire += weapon.m_fire * 0.25f;
        hit.m_damage.m_frost += weapon.m_frost * 0.25f;
        hit.m_damage.m_lightning += weapon.m_lightning * 0.25f;
        hit.m_damage.m_poison += weapon.m_poison * 0.25f;
        hit.m_damage.m_spirit += weapon.m_spirit * 0.25f;
    }

    private static float Ratio(Player player) => Mathf.Clamp(3f -
        LegendsMageAffinityPatch.Evocation(player) * 0.02f, 1f, 3f);

    private static void MaxEitr(Player __instance, ref float __result)
    {
        if (Has(__instance, Buff.Intellect)) __result += 1f;
    }

    private static void HaveEitr(Player __instance, float amount, ref bool __result)
    {
        if (!__result && Has(__instance, Buff.Intellect) &&
            __instance.GetEitr() + __instance.GetStamina() / Ratio(__instance) >= amount) __result = true;
    }

    private static bool UseEitr(Player __instance, ref float __0)
    {
        if (redirecting || __0 <= 0f || __instance != Player.m_localPlayer || !Has(__instance, Buff.Intellect))
            return true;
        var stamina = __instance.GetStamina();
        if (stamina <= 1f) return true;
        var paid = Mathf.Min(__0, stamina / Ratio(__instance));
        if (paid <= 0f) return true;
        try
        {
            redirecting = true;
            __instance.UseStamina(paid * Ratio(__instance));
        }
        finally { redirecting = false; }
        __0 -= paid;
        return __0 > 0f;
    }

    private static void ShieldDamage(Character __instance, HitData hit)
    {
        if (__instance is not Player player || player != Player.m_localPlayer || hit == null ||
            !Has(player, Buff.Shield)) return;
        var total = hit.m_damage.GetTotalDamage();
        if (total <= 0.1f) return;
        var affinity = LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Arcane);
        if (affinity == null || affinity.Charges < 1)
        {
            MageVisuals.Spawn("fx_VL_ParticleLightburst", player.GetCenterPoint(),
                Quaternion.LookRotation(Vector3.up));
            player.GetSEMan().RemoveStatusEffect(Names[(int)Buff.Shield].GetStableHashCode(), false);
            LegendsMageAffinityPatch.Cooldown("ManaShield", 20f *
                LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Intellect)));
            return;
        }
        affinity.Consume(1);
        var ratio = Mathf.Max(1f, 3f - LegendsMageAffinityPatch.Evocation(player) * (2f / 150f));
        var eitr = player.GetEitr();
        var staminaPool = Has(player, Buff.Intellect) ? player.GetStamina() / Ratio(player) : 0f;
        var absorbed = Mathf.Min(total, (eitr + staminaPool) / ratio);
        if (absorbed <= 0.1f) return;
        var cost = absorbed * ratio;
        if (staminaPool > 0f)
        {
            var staminaPaid = Mathf.Min(cost, staminaPool);
            player.UseStamina(staminaPaid * Ratio(player));
            cost -= staminaPaid;
        }
        if (cost > 0f) player.AddEitr(-cost);
        hit.ApplyModifier(Mathf.Clamp01(1f - absorbed / total));
        if (Time.time - lastShieldFx >= 0.15f)
        {
            lastShieldFx = Time.time;
            var position = player.GetCenterPoint();
            if (!MageVisuals.Spawn("fx_ShieldCharge_5", position) &&
                !MageVisuals.Spawn("fx_ShieldCharge_4", position))
                MageVisuals.Spawn("fx_ShieldCharge_3", position);
            if (!MageVisuals.Spawn("sfx_perfectblock", position))
                MageVisuals.Spawn("sfx_ice_hit", position);
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.AbjurationSkill,
            VL_Utility.GetShellSkillGain(player) * 0.1f);
    }

    private static void ShieldParry(Humanoid __instance, HitData hit, bool __result)
    {
        if (!__result || __instance is not Player player || player != Player.m_localPlayer) return;
        var timer = AccessTools.Field(typeof(Humanoid), "m_blockTimer");
        var interval = AccessTools.Field(typeof(Humanoid), "m_perfectBlockInterval");
        if (timer == null || interval == null) return;
        var elapsed = (float)timer.GetValue(__instance);
        if (elapsed < 0f || elapsed > (float)interval.GetValue(__instance)) return;
        ShieldDamage(player, hit);
    }

    private static bool ShieldDeath(Character __instance)
    {
        if (__instance is not Player player || player != Player.m_localPlayer || player.IsDead() ||
            player.GetHealth() > 0f || !Has(player, Buff.Shield)) return true;
        player.GetSEMan().RemoveStatusEffect(Names[(int)Buff.Shield].GetStableHashCode(), false);
        MageVisuals.Spawn("fx_VL_ParticleLightburst", player.GetCenterPoint(),
            Quaternion.LookRotation(Vector3.up));
        MageVisuals.Spawn("sfx_staff_lightning_fire", player.GetCenterPoint());
        MageVisuals.Spawn("fx_VL_Replica", player.GetCenterPoint());
        LegendsMageAffinityPatch.Cooldown("SE_VL_ManaShield",
            600f * VL_GlobalConfigs.c_priestBonusDyingLightCooldown *
            LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Intellect)));
        player.SetHealth(1f);
        player.Message(MessageHud.MessageType.Center, "Eitr Shield shattered!");
        return false;
    }
}

internal sealed class PerspexArcaneBuff : StatusEffect
{
    internal LegendsMageArcanePatch.Buff Kind;
    private float timer;

    public override void Setup(Character character)
    {
        base.Setup(character);
        if (Kind == LegendsMageArcanePatch.Buff.Intellect && character is Player player &&
            player == Player.m_localPlayer) player.AddEitr(1f);
    }

    public override void Stop()
    {
        base.Stop();
        if (Kind == LegendsMageArcanePatch.Buff.Intellect && m_character is Player player &&
            player == Player.m_localPlayer && player.GetEitr() > player.GetMaxEitr())
            player.AddEitr(player.GetMaxEitr() - player.GetEitr());
    }

    public override void UpdateStatusEffect(float dt)
    {
        if (m_icon == null) m_icon = MageVisuals.Icon(Kind == LegendsMageArcanePatch.Buff.Shield ?
            "StaminaUpgrade_Greydwarf" : Kind == LegendsMageArcanePatch.Buff.Intellect ?
            "HelmetPointyHat" : "Eitr");
        base.UpdateStatusEffect(dt);
        if (m_character is not Player player || player != Player.m_localPlayer ||
            Kind == LegendsMageArcanePatch.Buff.Shield) return;
        timer += dt;
        var interval = Kind == LegendsMageArcanePatch.Buff.Elemental ? 15f : 20f;
        if (timer < interval) return;
        timer = 0f;
        var affinity = LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Arcane);
        if (affinity != null && affinity.Charges >= 1) affinity.Consume(1);
        else
        {
            MageVisuals.Spawn("vfx_HitSparks", player.GetCenterPoint(), Quaternion.LookRotation(Vector3.up));
            MageVisuals.Spawn("sfx_lootspawn", player.GetCenterPoint(), Quaternion.LookRotation(Vector3.up));
            player.GetSEMan().RemoveStatusEffect(name.GetStableHashCode(), false);
        }
    }

    public override bool CanAdd(Character character) => character is Player &&
        ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Mage;
    public override bool IsDone() => ValheimLegends.ValheimLegends.vl_player?.vl_class !=
        ValheimLegends.ValheimLegends.PlayerClass.Mage;
}
