using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsCharmPatch
{
    private const string Control = "SE_VL_Charmcontrol";
    private const string Immunity = "SE_VL_CharmImmunity";

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(StatusEffect), "Setup"),
            postfix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(CharmSetup)));
        harmony.Patch(AccessTools.Method(typeof(SE_Charm), "UpdateStatusEffect"),
            prefix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(CharmTick)));
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(BeforeInput)),
            postfix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(AfterInput)));
        var add = AccessTools.Method(typeof(SEMan), "AddStatusEffect",
            new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float) });
        if (add != null) harmony.Patch(add,
            prefix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(PreventCharm)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.SetTamed)),
            prefix: new HarmonyMethod(typeof(LegendsCharmPatch), nameof(PreventTame)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        if (!__instance.m_StatusEffects.Exists(se => se != null && se.name == Control))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexCharmControl>());
        if (!__instance.m_StatusEffects.Exists(se => se != null && se.name == Immunity))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexCharmImmunity>());
    }

    private static void CharmSetup(StatusEffect __instance, Character character)
    {
        if (__instance is SE_Charm && character != null && !character.IsPlayer()) character.SetTamed(true);
    }

    private static void CharmTick(SE_Charm __instance)
    {
        if (__instance?.m_character == null || __instance.GetRemaningTime() > 1f) return;
        Release(__instance.m_character, __instance);
    }

    private static bool PreventCharm(SEMan __instance, StatusEffect __0)
    {
        if (__0 is not SE_Charm charm || !__instance.HaveStatusEffect(Immunity.GetStableHashCode()))
            return true;
        if (Traverse.Create(__instance).Field("m_character").GetValue<Character>() is { } victim)
            victim.m_faction = charm.originalFaction;
        return false;
    }

    private static bool PreventTame(Character __instance, bool __0) =>
        !__0 || !__instance.GetSEMan().HaveStatusEffect(Immunity.GetStableHashCode());

    internal static void Release(Character character, SE_Charm charm)
    {
        if (character.GetSEMan().HaveStatusEffect(Immunity.GetStableHashCode())) return;
        character.m_faction = charm.originalFaction;
        character.SetTamed(false);
        var immunity = ScriptableObject.CreateInstance<PerspexCharmImmunity>();
        immunity.m_ttl = Mathf.Clamp(character.GetHealthPercentage() *
            VL_GlobalConfigs.g_CooldownModifer * 60f, 5f, 300f);
        character.GetSEMan().AddStatusEffect(immunity);
    }

    private static void BeforeInput(Player player, ref bool __state)
    {
        __state = player == Player.m_localPlayer && !player.IsBlocking() &&
            VL_Utility.Ability2_Input_Down &&
            !player.GetSEMan().HaveStatusEffect("SE_VL_Ability2_CD".GetStableHashCode()) &&
            player.GetStamina() >= VL_Utility.GetCharmCost;
    }

    private static void AfterInput(Player player, bool __state)
    {
        if (!__state || player != Player.m_localPlayer ||
            Class_Enchanter.QueuedAttack != Class_Enchanter.EnchanterAttackType.Charm) return;
        var control = ScriptableObject.CreateInstance<PerspexCharmControl>();
        player.GetSEMan().AddStatusEffect(control, true);
    }
}

internal sealed class PerspexCharmControl : StatusEffect
{
    private bool released;

    public PerspexCharmControl()
    {
        name = "SE_VL_Charmcontrol";
        m_name = "Charm Control";
        m_tooltip = "Charm limit";
        m_ttl = 30f;
        m_icon = ZNetScene.instance?.GetPrefab("StaffSkeleton")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
    }

    public override void UpdateStatusEffect(float dt)
    {
        base.UpdateStatusEffect(dt);
        if (released || GetRemaningTime() > 1f) return;
        released = true;
        foreach (var character in Character.GetAllCharacters())
            if (character?.GetSEMan()?.GetStatusEffect("SE_VL_Charm".GetStableHashCode()) is SE_Charm charm)
            {
                LegendsCharmPatch.Release(character, charm);
                character.GetSEMan().RemoveStatusEffect(charm, false);
            }
    }
}

internal sealed class PerspexCharmImmunity : StatusEffect
{
    private float timer = 30f;
    private bool finished;

    public PerspexCharmImmunity()
    {
        name = "SE_VL_CharmImmunity";
        m_name = "Charm Immunity";
        m_tooltip = "Immune to charm";
        m_ttl = 30f;
        m_icon = ValheimLegends.ValheimLegends.Ability1_Sprite;
    }

    public override void UpdateStatusEffect(float dt)
    {
        base.UpdateStatusEffect(dt);
        if (m_character == null) return;
        timer -= dt;
        if (timer <= 0f)
        {
            timer = 3f;
            var pulse = ZNetScene.instance?.GetPrefab("fx_VL_AbsorbSpirit");
            if (pulse != null) UnityEngine.Object.Instantiate(pulse, m_character.GetCenterPoint(), Quaternion.identity);
        }
        if (finished || GetRemaningTime() > 3f) return;
        finished = true;
        var finish = ZNetScene.instance?.GetPrefab("vfx_crow_death");
        if (finish != null) UnityEngine.Object.Instantiate(finish, m_character.GetEyePoint(), Quaternion.identity);
    }
}
