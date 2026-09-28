using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMonkPowerUpPatch
{
    private const string UsesKey = "Perspex.Monk.ChiPowerUpUses";
    private const string CooldownName = "SE_VL_ChiPowerUp_CD";

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Monk), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsMonkPowerUpPatch), nameof(Input)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsMonkPowerUpPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsMonkPowerUpPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects != null &&
            !__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == CooldownName))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexChiPowerUpCooldown>());
    }

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer || !VL_Utility.Ability1_Input_Down || !player.IsBlocking()) return true;
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        if (!Class_Monk.PlayerIsUnarmed)
        {
            player.Message(MessageHud.MessageType.TopLeft, "Must be unarmed to use Chi Power Up.");
            return false;
        }
        var monk = player.GetSEMan().GetStatusEffect("SE_VL_Monk".GetStableHashCode()) as SE_Monk;
        if (monk == null) return false;
        if (player.GetSEMan().HaveStatusEffect(CooldownName.GetStableHashCode()))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Chi Power Up is recovering.");
            return false;
        }
        var cost = player.GetMaxStamina() * 0.8f;
        if (player.GetStamina() < cost)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Need {cost:0.#} stamina for Chi Power Up.");
            return false;
        }
        var cap = LegendsMonkSurgePatch.ChargeCap(player, monk);
        if (monk.hitCount >= cap)
        {
            player.Message(MessageHud.MessageType.TopLeft, "Chi charges are full.");
            return false;
        }
        monk.hitCount = Mathf.Min(monk.hitCount + 1, cap);
        LegendsMonkSurgePatch.Refresh(monk);
        player.UseStamina(cost);
        MageVisuals.Spawn("fx_VL_ChiPulse", player.GetCenterPoint());
        MageVisuals.Spawn("fx_Potion_frostresist", player.transform.position);
        var uses = player.m_customData.TryGetValue(UsesKey, out var saved) && int.TryParse(saved, out var count)
            ? Mathf.Clamp(count, 0, 2) + 1 : 1;
        if (uses == 3)
        {
            player.m_customData[UsesKey] = "0";
            var cooldown = ScriptableObject.CreateInstance<PerspexChiPowerUpCooldown>();
            cooldown.m_ttl = 120f;
            player.GetSEMan().AddStatusEffect(cooldown);
        }
        else player.m_customData[UsesKey] = uses.ToString();
        player.Message(MessageHud.MessageType.TopLeft, $"Chi Power Up: {monk.hitCount} Chi ({uses}/3).");
        return false;
    }
}

internal sealed class PerspexChiPowerUpCooldown : StatusEffect
{
    public PerspexChiPowerUpCooldown()
    {
        name = "SE_VL_ChiPowerUp_CD";
        m_name = "Chi Power Up Cooldown";
        m_tooltip = "Chi Power Up is recovering.";
        m_icon = SE_Monk.AbilityIcon;
    }

    public override void UpdateStatusEffect(float dt)
    {
        if (m_icon == null) m_icon = SE_Monk.AbilityIcon;
        base.UpdateStatusEffect(dt);
    }
}
