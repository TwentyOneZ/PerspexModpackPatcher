using System;
using System.Collections.Generic;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsMageAffinityPatch
{
    internal enum Focus { Fire, Frost, Arcane }
    private static readonly string[] Names =
    {
        "SE_VL_MageFireAffinity", "SE_VL_MageFrostAffinity", "SE_VL_MageArcaneAffinity"
    };
    private static readonly Dictionary<string, float> Cooldowns = new();
    private static bool meditating;
    private static Focus meditationFocus;
    private static float meditationTimer;
    private static Player current;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Class_Mage), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(Input)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(AfterInput)));
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.ValheimLegends), "NameCooldowns"),
            postfix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(NameAbilities)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(AffinityDamage)));
        harmony.Patch(AccessTools.Method(typeof(Humanoid), nameof(Humanoid.UseItem)),
            prefix: new HarmonyMethod(typeof(LegendsMageAffinityPatch), nameof(Thunderstone)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        for (var i = 0; i < Names.Length; i++)
        {
            var kind = (Focus)i;
            if (__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == Names[(int)kind])) continue;
            __instance.m_StatusEffects.Add(New(kind));
        }
    }

    private static PerspexMageAffinity New(Focus focus)
    {
        var effect = ScriptableObject.CreateInstance<PerspexMageAffinity>();
        effect.Kind = focus;
        effect.name = Names[(int)focus];
        effect.m_name = focus + " Affinity";
        effect.m_tooltip = "Regenerates " + focus + " charges.";
        var item = ZNetScene.instance?.GetPrefab(focus == Focus.Fire ? "StaffFireball" :
            focus == Focus.Frost ? "StaffIceShards" : "StaffShield")?.GetComponent<ItemDrop>();
        if (item != null) effect.m_icon = item.m_itemData.GetIcon();
        return effect;
    }

    internal static PerspexMageAffinity Get(Player player, Focus focus) =>
        player.GetSEMan().GetStatusEffect(Names[(int)focus].GetStableHashCode()) as PerspexMageAffinity;

    internal static Focus Current(Player player)
    {
        for (var i = 0; i < Names.Length; i++)
            if (Get(player, (Focus)i)?.Focused == true) return (Focus)i;
        return Focus.Arcane;
    }

    internal static float Evocation(Player player)
    {
        var level = LevelSystem.Instance;
        return player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill) *
               (1f + Mathf.Clamp(level.getAddCriticalChance() / 40f +
                                   level.getAddMagicDamage() / 80f, 0f, 0.5f));
    }

    internal static bool Ready(string ability) =>
        !Cooldowns.TryGetValue(ability, out var until) || Time.time >= until;

    internal static void Cooldown(string ability, float seconds) => Cooldowns[ability] = Time.time + seconds;

    private static void Ensure(Player player)
    {
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        foreach (Focus focus in Enum.GetValues(typeof(Focus)))
            if (Get(player, focus) == null) player.GetSEMan().AddStatusEffect(New(focus));
        if (Get(player, Focus.Fire)?.Focused != true && Get(player, Focus.Frost)?.Focused != true &&
            Get(player, Focus.Arcane) is { } arcane) arcane.Focused = true;
    }

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer) return true;
        if (current != player)
        {
            current = player;
            Cooldowns.Clear();
            meditating = false;
            LegendsMageFirePatch.Cancel();
            LegendsMageFrostPatch.Cancel();
        }
        Ensure(player);
        if (player.IsSitting())
        {
            LegendsMageFirePatch.Cancel();
            LegendsMageFrostPatch.Cancel();
            Meditate(player);
            return false;
        }
        meditating = false;
        meditationTimer = 0f;
        if (player.IsBlocking())
        {
            if (VL_Utility.Ability3_Input_Down) { SetFocus(player, Focus.Fire); return false; }
            if (VL_Utility.Ability2_Input_Down) { SetFocus(player, Focus.Frost); return false; }
            if (VL_Utility.Ability1_Input_Down) { SetFocus(player, Focus.Arcane); return false; }
        }
        switch (Current(player))
        {
            case Focus.Arcane: LegendsMageArcanePatch.Input(player); return false;
            case Focus.Frost: LegendsMageFrostPatch.Input(player); return false;
            case Focus.Fire: LegendsMageFirePatch.Input(player); return false;
            default: return false;
        }
    }

    private static void AfterInput(Player player)
    {
        if (player != Player.m_localPlayer) return;
        SyncVisualCooldowns(player);
    }

    private static void SyncVisualCooldowns(Player player)
    {
        var names = Current(player) switch
        {
            Focus.Fire => new[] { "Fireball", "FlameNova", "Meteor" },
            Focus.Frost => new[] { "IceShard", "FrostNova", "Blizzard" },
            _ => new[] { "SE_VL_ElementalMastery", "SE_VL_ArcaneIntellect", "SE_VL_ManaShield" }
        };
        for (var i = 0; i < 3; i++)
        {
            var id = ("SE_VL_Ability" + (i + 1) + "_CD").GetStableHashCode();
            var until = Cooldowns.TryGetValue(names[i], out var end) ? end : 0f;
            var remaining = until - Time.time;
            var effect = player.GetSEMan().GetStatusEffect(id);
            if (remaining <= 0f) { if (effect != null) player.GetSEMan().RemoveStatusEffect(id, false); continue; }
            if (effect == null)
            {
                effect = i switch
                {
                    0 => ScriptableObject.CreateInstance<SE_Ability1_CD>(),
                    1 => ScriptableObject.CreateInstance<SE_Ability2_CD>(),
                    _ => ScriptableObject.CreateInstance<SE_Ability3_CD>()
                };
                effect.m_ttl = remaining;
                player.GetSEMan().AddStatusEffect(effect);
            }
            else if (effect.m_ttl < remaining - 0.5f) effect.m_ttl = remaining;
        }
    }

    private static void Meditate(Player player)
    {
        Focus? focus = Held(ValheimLegends.ValheimLegends.Ability1_Hotkey.Value,
                ValheimLegends.ValheimLegends.Ability1_Hotkey_Combo.Value) ? Focus.Arcane :
            Held(ValheimLegends.ValheimLegends.Ability2_Hotkey.Value,
                ValheimLegends.ValheimLegends.Ability2_Hotkey_Combo.Value) ? Focus.Frost :
            VL_Utility.Ability3_Input_Pressed ? Focus.Fire : null;
        if (focus == null) { meditating = false; meditationTimer = 0f; return; }
        if (!meditating || focus != meditationFocus)
        {
            var nearby = new List<Character>();
            Character.GetCharactersInRange(player.transform.position, 30f, nearby);
            foreach (var character in nearby)
                if (BaseAI.IsEnemy(player, character) && !character.IsPlayer() &&
                    character.GetComponent<MonsterAI>()?.IsAlerted() == true)
                {
                    player.Message(MessageHud.MessageType.Center, "Cannot meditate with enemies nearby!");
                    return;
                }
            meditating = true;
            meditationFocus = focus.Value;
            meditationTimer = 0f;
        }
        var drain = 10f * Time.deltaTime;
        if (player.GetStamina() <= drain) return;
        player.UseStamina(drain);
        meditationTimer += Time.deltaTime;
        if (meditationTimer < 1f) return;
        meditationTimer = 0f;
        var affinity = Get(player, focus.Value);
        if (affinity == null || affinity.Charges >= affinity.MaxCharges) return;
        affinity.Add(1);
        MageVisuals.Spawn(focus == Focus.Arcane ? "fx_VL_ReplicaCreate" :
            focus == Focus.Frost ? "fx_Potion_frostresist" : "fx_VL_Flames",
            player.transform.position + Vector3.up);
        if (focus == Focus.Arcane) MageVisuals.Spawn("sfx_Potion_eitr_minor", player.transform.position);
    }

    private static bool Held(string key, string combo) =>
        key != "" && VL_Utility.GetHotkeyHeld(key) &&
        (combo == "" || VL_Utility.GetHotkeyHeld(combo));

    private static void SetFocus(Player player, Focus target)
    {
        var affinity = Get(player, target);
        if (affinity == null) return;
        if (affinity.Focused)
        {
            var cost = Mathf.Max(50f, player.GetMaxStamina() * 0.8f);
            if (player.GetStamina() >= cost && affinity.Charges < affinity.MaxCharges)
            {
                player.UseStamina(cost);
                affinity.Add(1);
                MageVisuals.Spawn(target == Focus.Arcane ? "vfx_Potion_eitr_minor" :
                    target == Focus.Frost ? "fx_Potion_frostresist" : "fx_VL_Flames",
                    player.transform.position);
                if (target == Focus.Arcane)
                    MageVisuals.Spawn("sfx_staff_lightning_charge", player.GetEyePoint());
            }
            return;
        }
        LegendsMageFirePatch.Cancel();
        LegendsMageFrostPatch.Cancel();
        foreach (Focus focus in Enum.GetValues(typeof(Focus)))
            if (Get(player, focus) is { } status) status.Focused = focus == target;
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        ValheimLegends.ValheimLegends.NameCooldowns();
        var icons = ValheimLegends.ValheimLegends.abilitiesStatus;
        if (icons != null)
        {
            foreach (var icon in icons) if (icon != null) UnityEngine.Object.Destroy(icon.gameObject);
            icons.Clear();
        }
        switch (target)
        {
            case Focus.Fire:
                MageVisuals.Spawn("vfx_FireAddFuel", player.GetCenterPoint());
                MageVisuals.Spawn("sfx_FireAddFuel", player.GetCenterPoint());
                break;
            case Focus.Frost:
                MageVisuals.Spawn("fx_iceshard_launch", player.GetCenterPoint(),
                    Quaternion.LookRotation(Vector3.up));
                break;
            case Focus.Arcane:
                MageVisuals.Spawn("fx_VL_ReplicaCreate", player.GetEyePoint());
                MageVisuals.Spawn("sfx_staff_lightning_charge", player.GetEyePoint());
                break;
        }
    }

    private static void NameAbilities()
    {
        var player = Player.m_localPlayer;
        if (player == null || ValheimLegends.ValheimLegends.vl_player?.vl_class !=
            ValheimLegends.ValheimLegends.PlayerClass.Mage) return;
        switch (Current(player))
        {
            case Focus.Fire:
                ValheimLegends.ValheimLegends.Ability1_Name = "Fireball";
                ValheimLegends.ValheimLegends.Ability2_Name = "F. Nova";
                ValheimLegends.ValheimLegends.Ability3_Name = "Meteor";
                break;
            case Focus.Frost:
                ValheimLegends.ValheimLegends.Ability1_Name = "Ice Shard";
                ValheimLegends.ValheimLegends.Ability2_Name = "Frost Nova";
                ValheimLegends.ValheimLegends.Ability3_Name = "Blizzard";
                break;
            case Focus.Arcane:
                ValheimLegends.ValheimLegends.Ability1_Name = "E. Mastery";
                ValheimLegends.ValheimLegends.Ability2_Name = "A. Intellect";
                ValheimLegends.ValheimLegends.Ability3_Name = "Eitr Shield";
                break;
        }
    }

    private static bool Thunderstone(Humanoid __instance, Inventory inventory, ItemDrop.ItemData item)
    {
        if (__instance is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Mage ||
            item?.m_shared?.m_name?.Contains("$item_thunderstone") != true) return true;
        inventory.RemoveOneItem(item);
        MageVisuals.Spawn("fx_VL_ParticleLightburst", player.GetEyePoint(),
            Quaternion.LookRotation(player.GetLookDir()));
        MageVisuals.Spawn("fx_VL_Shock", player.GetEyePoint() + player.GetLookDir() * 2.5f,
            Quaternion.LookRotation(player.GetLookDir()));
        MageVisuals.Animate(player, "gpower");
        Cooldowns.Clear();
        for (var i = 1; i <= 3; i++)
            player.GetSEMan().RemoveStatusEffect(("SE_VL_Ability" + i + "_CD").GetStableHashCode(), false);
        var maxRoll = 3 + Mathf.FloorToInt(Evocation(player) / 5f);
        foreach (Focus focus in Enum.GetValues(typeof(Focus)))
            Get(player, focus)?.Add(UnityEngine.Random.Range(1, maxRoll + 1));
        player.Message(MessageHud.MessageType.Center, "Surge: Cooldowns & Charges Restored!");
        return false;
    }

    private static void AffinityDamage(HitData hit)
    {
        if (hit == null || hit.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Mage)
            return;
        var chance = (5f + LevelSystem.Instance.getAddCriticalChance()) / 100f;
        if (UnityEngine.Random.value >= chance) return;
        var focus = Current(player);
        var level = Evocation(player);
        if (focus == Focus.Fire && hit.m_damage.GetTotalElementalDamage() > 0f)
        {
            var multiplier = 1f + level / 300f;
            hit.ApplyModifier(multiplier);
            player.Message(MessageHud.MessageType.TopLeft, $"Spell critical! ({multiplier:F1}x damage)");
        }
        else if (focus == Focus.Arcane && hit.GetTotalDamage() > 0f)
        {
            var candidates = new List<PerspexMageAffinity>();
            foreach (var other in new[] { Focus.Fire, Focus.Frost })
                if (Get(player, other) is { } status && status.Charges < status.RegenMax && status.Timer >= 1f)
                    candidates.Add(status);
            if (candidates.Count > 0)
            {
                var winner = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                winner.Timer = 30f;
                player.Message(MessageHud.MessageType.TopLeft, "Arcane Flux: " + winner.Kind + " Recharged!");
            }
            else if (Get(player, Focus.Arcane) is { } arcane)
            {
                if (arcane.Charges < arcane.RegenMax && arcane.Timer >= 1f) arcane.Timer = 30f;
                else if (arcane.Charges >= 1)
                {
                    arcane.Consume(1);
                    var multiplier = 1f + level / 75f;
                    hit.ApplyModifier(multiplier);
                    player.Message(MessageHud.MessageType.TopLeft, $"Arcane Surge! ({multiplier:F1}x damage)");
                }
            }
        }
    }
}

internal sealed class PerspexMageAffinity : StatusEffect
{
    internal LegendsMageAffinityPatch.Focus Kind;
    internal int Charges = 5;
    internal float Timer;
    internal bool Focused;
    internal int MaxCharges => Math.Min(30, 10 + Mathf.FloorToInt(
        LegendsMageAffinityPatch.Evocation((Player)m_character) / 7.5f));
    internal int RegenMax => Math.Min(30, 10 + Mathf.FloorToInt(
        ((Player)m_character).GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill) * 0.2f));

    public override void UpdateStatusEffect(float dt)
    {
        if (m_icon == null) m_icon = MageVisuals.Icon(Kind == LegendsMageAffinityPatch.Focus.Fire ?
            "StaffFireball" : Kind == LegendsMageAffinityPatch.Focus.Frost ? "StaffIceShards" : "StaffShield");
        if (m_character is not Player player || player != Player.m_localPlayer) return;
        if (Charges < RegenMax)
        {
            Timer += dt;
            var interval = player.GetSEMan().HaveStatusEffect(SEMan.s_statusEffectResting) ? 1f :
                Focused ? 10f : 30f;
            if (Timer >= interval) { Add(1); Timer = 0f; }
        }
        m_name = Kind + " Affinity: " + Charges + "/" + RegenMax + (Focused ? " *" : "");
        base.UpdateStatusEffect(dt);
    }

    internal void Add(int amount) => Charges = Mathf.Min(Charges + amount, MaxCharges);
    internal void Consume(int amount) => Charges = Math.Max(0, Charges - amount);
    public override bool CanAdd(Character character) => character is Player &&
        ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Mage;
    public override bool IsDone() => ValheimLegends.ValheimLegends.vl_player?.vl_class !=
        ValheimLegends.ValheimLegends.PlayerClass.Mage;
}

internal static class MageVisuals
{
    internal static Sprite Icon(string item) =>
        ZNetScene.instance?.GetPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData?.GetIcon();

    internal static bool Spawn(string prefab, Vector3 position, Quaternion? rotation = null)
    {
        var effect = ZNetScene.instance?.GetPrefab(prefab);
        if (effect == null) return false;
        UnityEngine.Object.Instantiate(effect, position, rotation ?? Quaternion.identity);
        return true;
    }

    internal static void Animate(Player player, string trigger, float? speed = null)
    {
        var animation = Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>();
        animation?.SetTrigger(trigger);
        if (speed.HasValue) animation?.SetSpeed(speed.Value);
    }
}
