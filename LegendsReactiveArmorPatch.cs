using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsReactiveArmorPatch
{
    private const string ArmorName = "SE_VL_Reactivearmor";
    private const string CooldownName = "SE_VL_CDReactivearmor";
    private static readonly int ArmorHash = ArmorName.GetStableHashCode();
    private static readonly int CooldownHash = CooldownName.GetStableHashCode();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsReactiveArmorPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsReactiveArmorPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Class_Metavoker), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsReactiveArmorPatch), nameof(Input)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsReactiveArmorPatch), nameof(Absorb)) { priority = Priority.First });
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        if (!__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == ArmorName))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexReactiveArmor>());
        if (!__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == CooldownName))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexReactiveCooldown>());
    }

    private static float Cooldown() => VL_Utility.GetLightCooldownTime;
    private static float Cost() => 20f * VL_GlobalConfigs.g_EnergyCostModifer *
                                   LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Agility));

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer || !player.IsBlocking()) return true;
        if (VL_Utility.Ability1_Input_Down) { Activate(player); return false; }
        if (VL_Utility.Ability3_Input_Down) { Release(player); return false; }
        return true;
    }

    private static void Activate(Player player)
    {
        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect(CooldownHash))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Reactive Armor is not ready.");
            return;
        }
        var cost = Cost();
        if (player.GetStamina() < cost)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {cost:0.#} stamina for Reactive Armor.");
            return;
        }
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        player.UseStamina(cost);
        var cooldown = ScriptableObject.CreateInstance<PerspexReactiveCooldown>();
        cooldown.m_ttl = Cooldown() * 6f;
        effects.AddStatusEffect(cooldown);
        if (effects.HaveStatusEffect(ArmorHash)) effects.RemoveStatusEffect(ArmorHash, false);
        effects.AddStatusEffect(ScriptableObject.CreateInstance<PerspexReactiveArmor>());
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        Effect(player, "fx_VL_ParticleLightburst");
        player.RaiseSkill(ValheimLegends.ValheimLegends.AbjurationSkill, VL_Utility.GetLightSkillGain * 3f);
    }

    private static void Release(Player player)
    {
        var effects = player.GetSEMan();
        if (effects.GetStatusEffect(ArmorHash) is not PerspexReactiveArmor armor)
        {
            player.Message(MessageHud.MessageType.TopLeft, "Reactive Armor is not up.");
            return;
        }
        var charges = armor.Charges;
        foreach (var target in Character.GetAllCharacters())
        {
            if (charges <= 0) break;
            if (target == null || !BaseAI.IsEnemy(player, target) ||
                Vector3.Distance(player.transform.position, target.transform.position) > 6f ||
                !VL_Utility.LOS_IsValid(target, player.transform.position, player.GetCenterPoint())) continue;
            target.Stagger(target.transform.position - player.transform.position);
            Effect(target, "fx_VL_ForwardLightningShock");
            charges--;
        }
        effects.RemoveStatusEffect(ArmorHash, false);
        var cooldown = effects.GetStatusEffect(CooldownHash);
        if (cooldown != null)
            cooldown.m_ttl = Mathf.Max(Cooldown() * 3f,
                cooldown.m_ttl - Cooldown() * 3f * charges / armor.MaxCharges);
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        Effect(player, "fx_VL_Lightburst");
    }

    private static void Absorb(Character __instance, HitData hit)
    {
        if (__instance is not Player player || player != Player.m_localPlayer || hit == null ||
            player.GetSEMan().GetStatusEffect(ArmorHash) is not PerspexReactiveArmor armor ||
            armor.Charges <= 0) return;
        var damage = hit.m_damage.GetTotalDamage();
        if (damage <= 0f) return;
        var cost = damage * 2f * armor.StaminaModifier *
                   (1f - LevelSystem.Instance.getStaminaReduction() / 100f);
        if (cost <= 0f) return;
        var available = player.GetStamina();
        var covered = Mathf.Clamp01(available / cost);
        player.UseStamina(Mathf.Min(available, cost));
        hit.ApplyModifier(1f - covered);
        if (!hit.m_ranged && hit.GetAttacker() is Character attacker)
        {
            attacker.Stagger(-hit.m_dir);
            Effect(attacker, "fx_VL_ForwardLightningShock");
            Effect(attacker, "fx_VL_ParticleLightSuction");
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.AbjurationSkill,
            VL_Utility.GetWarpSkillGain * 0.25f);
        armor.Charges = covered >= 1f ? armor.Charges - 1 : 0;
        Effect(player, "fx_VL_ParticleLightburst");
        if (armor.Charges > 0) return;
        Effect(player, "fx_VL_ParticleLightSuction");
        Effect(player, "fx_VL_Lightburst");
        player.GetSEMan().RemoveStatusEffect(ArmorHash, false);
        var cooldown = player.GetSEMan().GetStatusEffect(CooldownHash);
        if (cooldown != null) cooldown.m_ttl = Mathf.Min(cooldown.m_ttl, Cooldown() * 3f);
    }

    private static void Effect(Character target, string prefabName)
    {
        var prefab = ZNetScene.instance?.GetPrefab(prefabName);
        if (prefab != null) UnityEngine.Object.Instantiate(prefab, target.GetCenterPoint(), Quaternion.identity);
    }
}

internal sealed class PerspexReactiveArmor : StatusEffect
{
    internal int Charges = 3;
    internal int MaxCharges = 3;
    internal float StaminaModifier = 1f;

    public PerspexReactiveArmor()
    {
        name = "SE_VL_Reactivearmor";
        m_name = "Reactive Armor";
        m_tooltip = "Absorbs hits using stamina. Release remaining charges while blocking.";
        m_icon = MageVisuals.Icon("StaffShield");
    }

    public override void Setup(Character character)
    {
        base.Setup(character);
        var level = LevelSystem.Instance;
        var skill = character.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill);
        var power = skill * (1f + Mathf.Clamp(level.getAddHp() / 400f + level.getAddStamina() / 200f, 0f, 0.5f));
        MaxCharges = 3 + Mathf.RoundToInt(Mathf.Sqrt(power * 2f));
        Charges = MaxCharges;
        StaminaModifier = 1f - power / 300f;
    }

    public override void UpdateStatusEffect(float dt)
    {
        if (m_icon == null) m_icon = MageVisuals.Icon("StaffShield");
        m_ttl = Charges;
        m_time = 0f;
        base.UpdateStatusEffect(dt);
    }

    public override bool CanAdd(Character character) => character is Player &&
        ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Metavoker;

    public override bool IsDone() => Charges <= 0 ||
        ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Metavoker;
}

internal sealed class PerspexReactiveCooldown : StatusEffect
{
    public PerspexReactiveCooldown()
    {
        name = "SE_VL_CDReactivearmor";
        m_name = "Reactive Armor Cooldown";
        m_tooltip = "Reactive Armor is recovering.";
        m_icon = MageVisuals.Icon("StaffShield");
    }

    public override void UpdateStatusEffect(float dt)
    {
        if (m_icon == null) m_icon = MageVisuals.Icon("StaffShield");
        base.UpdateStatusEffect(dt);
    }
}
