using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsWindfuryPatch
{
    private const string Cooldown = "SE_VL_Windfury_CD";
    private static bool repeating;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(OnDamage)));
        harmony.Patch(AccessTools.Method(typeof(Character), "CheckDeath"),
            prefix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(BeforeDeath)),
            postfix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(AfterDeath)));
        harmony.Patch(AccessTools.Method(typeof(Class_Shaman), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(BeforeInput)),
            postfix: new HarmonyMethod(typeof(LegendsWindfuryPatch), nameof(AfterInput)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null ||
            __instance.m_StatusEffects.Exists(se => se != null && se.name == Cooldown)) return;
        var effect = ScriptableObject.CreateInstance<StatusEffect>();
        effect.name = Cooldown;
        effect.m_name = "Windfury Cooldown";
        effect.m_tooltip = "Windfury recharges when a creature dies nearby or Spirit Bomb is cast.";
        __instance.m_StatusEffects.Add(effect);
    }

    private static void OnDamage(Character __instance, HitData hit)
    {
        if (repeating || __instance == null || hit == null || hit.m_ranged ||
            hit.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Shaman ||
            player.GetSEMan().HaveStatusEffect(Cooldown.GetStableHashCode())) return;
        var weapon = player.GetCurrentWeapon();
        if (weapon == null || !weapon.IsWeapon() || weapon.m_shared.m_name.ToLowerInvariant() == "unarmed") return;
        var level = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill) *
                    (1f + Mathf.Clamp(LevelSystem.Instance.getAddCriticalChance() / 40f +
                                      LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));
        if (Random.value >= 0.05f + level / 800f) return;
        var bonus = new HitData();
        bonus.m_pushForce = weapon.GetDeflectionForce();
        bonus.m_dir = (player.transform.position - __instance.transform.position).normalized;
        bonus.m_point = player.GetEyePoint();
        bonus.m_damage = hit.m_damage;
        bonus.ApplyModifier(0.3f + level / 160f);
        bonus.SetAttacker(player);
        try
        {
            repeating = true;
            __instance.Damage(bonus);
            __instance.Damage(bonus);
        }
        finally { repeating = false; }
        var effect = ScriptableObject.CreateInstance<StatusEffect>();
        effect.name = Cooldown;
        effect.m_name = "Windfury Cooldown";
        player.GetSEMan().AddStatusEffect(effect);
    }

    private static void BeforeDeath(Character __instance, ref bool __state) =>
        __state = __instance != null && !__instance.IsDead() && __instance.GetHealth() <= 0f;

    private static void AfterDeath(Character __instance, bool __state)
    {
        if (!__state || __instance == null || __instance.IsPlayer()) return;
        var player = Player.m_localPlayer;
        if (player == null || ValheimLegends.ValheimLegends.vl_player?.vl_class !=
            ValheimLegends.ValheimLegends.PlayerClass.Shaman ||
            Vector3.Distance(player.transform.position, __instance.transform.position) > 10f) return;
        player.AddStamina(25f * VL_GlobalConfigs.c_shamanBonusSpiritGuide);
        Clear(player);
        for (var slot = 1; slot <= 3; slot++)
            if (player.GetSEMan().GetStatusEffect(("SE_VL_Ability" + slot + "_CD").GetStableHashCode()) is { } cd)
                cd.m_ttl *= 0.7f;
    }

    private static void BeforeInput(Player player, ref bool __state) =>
        __state = player == Player.m_localPlayer && !player.IsBlocking() &&
            VL_Utility.Ability3_Input_Down &&
            !player.GetSEMan().HaveStatusEffect("SE_VL_Ability3_CD".GetStableHashCode());

    private static void AfterInput(Player player, bool __state)
    {
        if (__state && player.GetSEMan().HaveStatusEffect("SE_VL_Ability3_CD".GetStableHashCode())) Clear(player);
    }

    private static void Clear(Player player) =>
        player.GetSEMan().RemoveStatusEffect(Cooldown.GetStableHashCode(), false);
}
