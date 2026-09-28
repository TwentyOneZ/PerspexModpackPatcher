using System;
using System.Collections.Generic;
using System.Linq;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDeepNorthPatch
{
    private const string EffectName = "SE_VL_BiomeDeepNorth";
    private static readonly int EffectHash = EffectName.GetStableHashCode();

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(ZoneCharge)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(Character), "UpdateWalking"),
            prefix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(WalkingStart)),
            postfix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(WalkingEnd)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.ApplyDamage)),
            prefix: new HarmonyMethod(typeof(LegendsDeepNorthPatch), nameof(LastShelter)));
    }

    private static PerspexDeepNorth Create()
    {
        var effect = ScriptableObject.CreateInstance<PerspexDeepNorth>();
        effect.name = EffectName;
        effect.m_name = "Biome: Deep North";
        effect.m_tooltip = "Firm footing on slippery ice. Deep snow does not slow you. A fatal blow leaves you at 1 Health and consumes this buff.";
        effect.m_ttl = 600f;
        var mountain = ObjectDB.instance?.GetStatusEffect("SE_VL_BiomeMountain".GetStableHashCode());
        if (mountain != null) effect.m_icon = mountain.m_icon;
        if (effect.m_icon == null)
            effect.m_icon = ObjectDB.instance?.GetItemPrefab("FreezeGland")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
        return effect;
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance?.m_StatusEffects == null || __instance.m_StatusEffects.Exists(effect => effect?.name == EffectName)) return;
        __instance.m_StatusEffects.Add(Create());
    }

    private static bool ZoneCharge(Player player, float altitude)
    {
        if (player != Player.m_localPlayer || player.GetCurrentBiome() != Heightmap.Biome.DeepNorth ||
            !Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCharging").GetValue<bool>() ||
            !ValheimLegends.ValheimLegends.isChanneling ||
            !(VL_Utility.Ability3_Input_Up || player.GetStamina() <= 1f ||
              player.GetStamina() <= VL_Utility.GetZoneChargeCostPerUpdate ||
              Mathf.Max(0f, altitude - player.transform.position.y) >= 1f)) return true;

        var attributes = LevelSystem.Instance;
        var abjuration = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill) *
            (1f + Mathf.Clamp(attributes.getAddHp() / 400f + attributes.getAddStamina() / 200f, 0f, 0.5f));
        var scale = attributes.getLevel() * 10f / 6f * (1f + abjuration / 300f);
        var charge = Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCount").GetValue<float>();
        var duration = 600f + (3f * scale + 2f * charge) * VL_GlobalConfigs.c_enchanterBiome;
        var radius = 30f + 0.2f * abjuration;
        var nearby = new List<Character>();
        Character.GetCharactersInRange(player.transform.position, radius, nearby);
        foreach (var target in nearby)
        {
            if (target == null || BaseAI.IsEnemy(target, player)) continue;
            var effect = Create();
            effect.m_ttl = duration;
            if (target == player || !target.IsPlayer()) target.GetSEMan().AddStatusEffect(effect, resetTime: true);
            else target.GetSEMan().AddStatusEffect(EffectHash, resetTime: true);
            var vfx = ZNetScene.instance?.GetPrefab("fx_Potion_frostresist");
            if (vfx != null) UnityEngine.Object.Instantiate(vfx, target.GetCenterPoint(), Quaternion.identity);
        }
        var burst = ZNetScene.instance?.GetPrefab("fx_VL_ParticleFieldBurst");
        if (burst != null) UnityEngine.Object.Instantiate(burst, player.transform.position, Quaternion.identity);
        Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCharging").SetValue(false);
        Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCount").SetValue(0f);
        Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeChargeAmount").SetValue(0f);
        var skillGain = Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeSkillGain");
        player.RaiseSkill(ValheimLegends.ValheimLegends.AbjurationSkill, skillGain.GetValue<float>());
        skillGain.SetValue(0f);
        ValheimLegends.ValheimLegends.isChanneling = false;
        Class_Enchanter.QueuedAttack = Class_Enchanter.EnchanterAttackType.None;
        return false;
    }

    private static void WalkingStart(Character __instance, ref (bool active, bool shoes, float snow) __state)
    {
        if (__instance?.GetSEMan()?.HaveStatusEffect(EffectHash) != true) return;
        var shoes = Traverse.Create(__instance).Field("m_iceShoes");
        __state = (true, shoes.GetValue<bool>(), __instance.m_deepSnowSlowMax);
        shoes.SetValue(true);
        __instance.m_deepSnowSlowMax = 0f;
    }

    private static void WalkingEnd(Character __instance, (bool active, bool shoes, float snow) __state)
    {
        if (!__state.active) return;
        Traverse.Create(__instance).Field("m_iceShoes").SetValue(__state.shoes);
        __instance.m_deepSnowSlowMax = __state.snow;
    }

    private static void LastShelter(Character __instance, HitData hit)
    {
        if (__instance is not Player player || hit == null ||
            !player.GetSEMan().HaveStatusEffect(EffectHash) || player.GetHealth() <= 0f) return;
        var incoming = hit.GetTotalDamage() * Game.m_localDamgeTakenRate;
        if (incoming < player.GetHealth()) return;
        hit.ApplyModifier(Mathf.Max(0f, player.GetHealth() - 1f) / Mathf.Max(incoming, 0.001f));
        player.GetSEMan().RemoveStatusEffect(EffectHash, false);
        var vfx = ZNetScene.instance?.GetPrefab("fx_Potion_frostresist");
        if (vfx != null) UnityEngine.Object.Instantiate(vfx, player.GetCenterPoint(), Quaternion.identity);
        player.Message(MessageHud.MessageType.TopLeft, "Last Shelter saved you.");
    }
}

internal sealed class PerspexDeepNorth : StatusEffect
{
    public override void Setup(Character character)
    {
        base.Setup(character);
        foreach (var other in character.GetSEMan().GetStatusEffects().ToArray())
            if (other != this && other?.name?.StartsWith("SE_VL_Biome", StringComparison.Ordinal) == true)
                character.GetSEMan().RemoveStatusEffect(other, false);
    }
}
