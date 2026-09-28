using HarmonyLib;
using UnityEngine;

namespace PerspexModpackPatcher;

internal static class LegendsRootedPatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsRootedPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsRootedPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Projectile), "OnHit"),
            postfix: new HarmonyMethod(typeof(LegendsRootedPatch), nameof(OnHit)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects != null &&
            !__instance.m_StatusEffects.Exists(se => se != null && se.name == "SE_VL_Rooted"))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexRooted>());
    }

    private static void OnHit(Projectile __instance, Collider collider, Character ___m_owner)
    {
        if (__instance == null || collider == null ||
            __instance.name != "VL_DruidRoot" && __instance.name != "Root") return;
        var hitObject = Projectile.FindHitObject(collider);
        var target = hitObject?.GetComponent<Character>() ?? hitObject?.GetComponentInParent<Character>();
        if (target != null && !target.IsPlayer() && !target.m_boss)
            target.GetSEMan().AddStatusEffect(ScriptableObject.CreateInstance<PerspexRooted>(), true);
        if (___m_owner is Player player &&
            player.GetSEMan().GetStatusEffect("SE_VL_Ability1_CD".GetStableHashCode()) is { } cooldown)
            cooldown.m_ttl *= 0.99f;
    }
}

internal sealed class PerspexRooted : StatusEffect
{
    public PerspexRooted()
    {
        name = "SE_VL_Rooted";
        m_name = "Rooted";
        m_tooltip = "Rooted";
        m_ttl = 1f;
        m_icon = ValheimLegends.ValheimLegends.Ability3_Sprite;
    }

    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        speed = 0f;
        base.ModifySpeed(baseSpeed, ref speed, character, dir);
    }
}
