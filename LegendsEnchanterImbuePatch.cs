using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsEnchanterImbuePatch
{
    internal enum Kind { FlameWeapon, IceWeapon, ThunderWeapon, FlameArmor, IceArmor, ThunderArmor }
    private static readonly string[] Names =
    {
        "SE_VL_FlameWeapon", "SE_VL_IceWeapon", "SE_VL_ThunderWeapon",
        "SE_VL_FlameArmor", "SE_VL_IceArmor", "SE_VL_ThunderArmor"
    };
    private static bool chaining;

    private static void Effect(string name, Vector3 position, Quaternion? rotation = null)
    {
        var prefab = ZNetScene.instance?.GetPrefab(name);
        if (prefab != null) UnityEngine.Object.Instantiate(prefab, position, rotation ?? Quaternion.identity);
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy,
            postfix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(Input)) { priority = Priority.First },
            postfix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(AfterInput)));
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), nameof(Class_Enchanter.HasZoneBuffTime)),
            prefix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(FindZoneBuff)));
        harmony.Patch(AccessTools.Method(typeof(Class_Enchanter), "Execute_Attack"),
            transpiler: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(ShockDamageCall)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(OnDamage)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(VL_Utility), "GetZoneChargeCostPerUpdate"),
            postfix: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(ZoneDrain)));
        var damage = AccessTools.Method(typeof(ValheimLegends.ValheimLegends).GetNestedType(
            "VL_Damage_Patch", BindingFlags.Public | BindingFlags.NonPublic), "Prefix");
        harmony.Patch(damage,
            transpiler: new HarmonyMethod(typeof(LegendsEnchanterImbuePatch), nameof(RemoveOldTouch)));
    }

    private static void ZoneDrain(ref float __result) =>
        __result = VL_Utility.GetZoneChargeCost * Time.deltaTime;

    private static bool FindZoneBuff(Player p, ref StatusEffect __result)
    {
        __result = null;
        if (p?.GetSEMan() == null) return false;
        foreach (var effect in p.GetSEMan().GetStatusEffects())
            if (effect?.name != null && effect.name.StartsWith("SE_VL_Biome", StringComparison.Ordinal))
            {
                __result = effect;
                break;
            }
        return false;
    }

    private static IEnumerable<CodeInstruction> RemoveOldTouch(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(VL_GlobalConfigs) &&
                method.Name == "get_c_enchanterBonusElementalTouch")
            {
                instruction.opcode = OpCodes.Ldc_R4;
                instruction.operand = 0f;
                count++;
            }
            yield return instruction;
        }
        if (count != 3) throw new InvalidOperationException($"Unexpected Enchanter touch sites: {count}");
    }

    private static IEnumerable<CodeInstruction> ShockDamageCall(IEnumerable<CodeInstruction> source)
    {
        var count = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsEnchanterImbuePatch), nameof(ShockDamage));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Zone Shock damage sites: {count}");
    }

    private static void ShockDamage(Character target, HitData hit)
    {
        var player = Player.m_localPlayer;
        var buff = player == null ? null : Class_Enchanter.HasZoneBuffTime(player);
        if (buff != null)
        {
            var attributes = LevelSystem.Instance;
            var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
            var amount = LegendsEconomyPatch.Magic(skill, attributes.getParameter(Parameter.Special), 0.80f,
                VL_GlobalConfigs.c_enchanterBiomeShock);
            hit.m_damage.m_lightning = amount *
                (1f + Mathf.Clamp01(buff.GetRemaningTime() / Mathf.Max(1f, buff.m_ttl)));
            hit.SetAttacker(player);
        }
        target.Damage(hit);
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        foreach (Kind kind in Enum.GetValues(typeof(Kind)))
            if (!__instance.m_StatusEffects.Exists(se => se != null && se.name == Names[(int)kind]))
                __instance.m_StatusEffects.Add(New(kind));
    }

    private static PerspexEnchantment New(Kind kind)
    {
        var effect = ScriptableObject.CreateInstance<PerspexEnchantment>();
        effect.Kind = kind;
        effect.name = Names[(int)kind];
        effect.m_name = kind switch
        {
            Kind.FlameWeapon => "Flame Weapon", Kind.IceWeapon => "Ice Weapon",
            Kind.ThunderWeapon => "Thunder Weapon", Kind.FlameArmor => "Flame Armor",
            Kind.IceArmor => "Ice Armor", _ => "Thunder Armor"
        };
        effect.m_tooltip = kind switch
        {
            Kind.FlameWeapon => "Attacks are imbued with Fire.",
            Kind.IceWeapon => "Attacks are imbued with Frost.",
            Kind.ThunderWeapon => "Attacks are imbued with Lightning.",
            Kind.FlameArmor => "Cauterizes wounds and reduces magical damage.",
            Kind.IceArmor => "Reduces physical damage and slows attackers.",
            _ => "Increases speed and counters attacks with lightning."
        };
        var iconPrefab = kind switch
        {
            Kind.FlameWeapon => "StaffFireball", Kind.IceWeapon => "StaffIceShards",
            Kind.ThunderWeapon => "DragonTear", Kind.FlameArmor => "SurtlingCore",
            Kind.IceArmor => "FreezeGland", _ => "Thunderstone"
        };
        var icon = ZNetScene.instance?.GetPrefab(iconPrefab)?.GetComponent<ItemDrop>();
        if (icon != null) effect.m_icon = icon.m_itemData.GetIcon();
        return effect;
    }

    private static PerspexEnchantment Get(Player player, Kind kind) =>
        player.GetSEMan().GetStatusEffect(Names[(int)kind].GetStableHashCode()) as PerspexEnchantment;

    internal static void ArmorCharge(Player player, Kind armor, Vector3 point)
    {
        var weapon = (Kind)((int)armor - 3);
        if (Get(player, weapon) is not { } effect) return;
        effect.Stacks = Mathf.Min(100, effect.Stacks + 1);
        var evo = Evo(player);
        if (UnityEngine.Random.Range(0f, 100f) <= effect.Stacks + 1f + evo / 100f)
        {
            effect.Stacks = 0;
            Proc(player, weapon, point, evo, Abj(player));
        }
    }

    private static bool Input(Player player, ref bool __state)
    {
        __state = Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCharging").GetValue<bool>();
        if (player != Player.m_localPlayer) return true;
        if (VL_Utility.Ability3_Input_Down && player.IsSitting())
        {
            LegendsClassRepairPatch.Repair(player, LegendsClassRepairPatch.Kind.Enchanter);
            return false;
        }
        if (VL_Utility.Ability3_Input_Down && player.IsBlocking() &&
            Class_Enchanter.HasZoneBuffTime(player) == null)
        {
            var cooldown = player.GetSEMan().GetStatusEffect("SE_VL_Ability3_CD".GetStableHashCode());
            var thunderstone = player.GetInventory().GetItem("$item_thunderstone");
            if (cooldown != null && thunderstone != null)
            {
                player.GetInventory().RemoveOneItem(thunderstone);
                foreach (var character in Character.GetAllCharacters())
                    if (character != null && character.IsPlayer() &&
                        Vector3.Distance(character.transform.position, player.transform.position) <= 60f)
                        foreach (var slot in new[] { 1, 2, 3 })
                            if (character.GetSEMan().GetStatusEffect(("SE_VL_Ability" + slot + "_CD").GetStableHashCode()) is { } cd)
                                cd.m_ttl *= 0.01f;
                player.Message(MessageHud.MessageType.TopLeft, "Consumed one Thunderstone.");
                Effect("fx_VL_ParticleLightburst", player.GetEyePoint(), Quaternion.LookRotation(player.GetLookDir()));
                Effect("fx_VL_Shock", player.GetEyePoint() + player.GetLookDir() * 2.5f,
                    Quaternion.LookRotation(player.GetLookDir()));
            }
            else player.Message(MessageHud.MessageType.TopLeft, "Need an active Zone Charge effect or a Thunderstone.");
            return false;
        }
        if (VL_Utility.Ability3_Input_Down && !__state && !player.IsBlocking() &&
            Class_Enchanter.HasZoneBuffTime(player) != null)
            return false;
        if (!player.IsBlocking()) return true;
        if (VL_Utility.Ability1_Input_Down)
        {
            Cycle(player, false);
            return false;
        }
        if (VL_Utility.Ability2_Input_Down)
        {
            Cycle(player, true);
            return false;
        }
        return true;
    }

    private static void AfterInput(Player player, bool __state)
    {
        if (player != Player.m_localPlayer) return;
        if (!Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeCharging").GetValue<bool>()) return;
        if (!__state)
        {
            Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeChargeAmountMax")
                .SetValue(Mathf.RoundToInt(15f * (1f - Abj(player) * 0.005f)));
            return;
        }
        if (!VL_Utility.Ability3_Input_Pressed) return;
        var field = Traverse.Create(typeof(Class_Enchanter)).Field("zonechargeChargeAmount");
        var amount = field.GetValue<int>();
        if (amount > 0) field.SetValue(amount + Mathf.Max(0, Mathf.RoundToInt(Time.deltaTime * 60f) - 1));
    }

    private static void Cycle(Player player, bool weapon)
    {
        var first = weapon ? Kind.FlameWeapon : Kind.FlameArmor;
        var next = first;
        for (var i = 0; i < 3; i++)
        {
            var current = (Kind)((int)first + i);
            if (Get(player, current) == null) continue;
            player.GetSEMan().RemoveStatusEffect(Names[(int)current].GetStableHashCode(), false);
            next = i == 2 ? (Kind)(-1) : (Kind)((int)current + 1);
            break;
        }
        if ((int)next >= 0) player.GetSEMan().AddStatusEffect(New(next));
        var point = player.GetCenterPoint();
        switch (next)
        {
            case Kind.FlameWeapon: Effect("vfx_Potion_health_medium", point); break;
            case Kind.IceWeapon: Effect("fx_Potion_frostresist", point); break;
            case Kind.ThunderWeapon: Effect("vfx_Potion_stamina_medium", point); break;
            case Kind.FlameArmor: Effect("fx_VL_Flames", player.transform.position); break;
            case Kind.IceArmor: Effect("fx_guardstone_activate", player.transform.position); break;
            case Kind.ThunderArmor:
                Effect("fx_VL_ParticleLightburst", player.GetEyePoint(), Quaternion.LookRotation(player.GetLookDir()));
                Effect("fx_VL_Shock", player.GetEyePoint() + player.GetLookDir() * 2.5f +
                    player.transform.right * 0.25f, Quaternion.LookRotation(player.GetLookDir()));
                break;
        }
        if (!weapon && (int)next >= 0)
            Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>()?.SetTrigger("gpower");
        if (!player.GetSEMan().HaveStatusEffect((weapon ? "SE_VL_Ability2_CD" : "SE_VL_Ability1_CD").GetStableHashCode()))
        {
            var cooldown = weapon ? (StatusEffect)ScriptableObject.CreateInstance<SE_Ability2_CD>() :
                ScriptableObject.CreateInstance<SE_Ability1_CD>();
            cooldown.m_ttl = 0.1f;
            player.GetSEMan().AddStatusEffect(cooldown);
        }
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
    }

    private static float Evo(Player player) =>
        player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill) *
        (1f + Mathf.Clamp(LevelSystem.Instance.getAddCriticalChance() / 40f +
                          LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));

    private static float Abj(Player player) =>
        player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill) *
        (1f + Mathf.Clamp(LevelSystem.Instance.getAddHp() / 400f +
                          LevelSystem.Instance.getAddStamina() / 200f, 0f, 0.5f));

    private static void OnDamage(Character __instance, HitData hit)
    {
        if (chaining || hit == null || __instance == null || hit.GetTotalDamage() <= 0f ||
            hit.GetAttacker() is not Player player || player != Player.m_localPlayer ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Enchanter)
            return;
        foreach (var status in player.GetSEMan().GetStatusEffects())
            if (status.m_name.StartsWith("Enchant ", StringComparison.Ordinal))
            {
                player.GetSEMan().RemoveStatusEffect(status, true);
                break;
            }
        var kind = Get(player, Kind.FlameWeapon) != null ? Kind.FlameWeapon :
            Get(player, Kind.IceWeapon) != null ? Kind.IceWeapon :
            Get(player, Kind.ThunderWeapon) != null ? Kind.ThunderWeapon : (Kind)(-1);
        if ((int)kind < 0) return;
        var effect = Get(player, kind);
        var level = LevelSystem.Instance.getLevel();
        var evo = Evo(player);
        var factor = (1f + evo / 150f) * VL_GlobalConfigs.g_DamageModifer;
        switch (kind)
        {
            case Kind.FlameWeapon:
                hit.m_damage.m_fire += level / 6f * UnityEngine.Random.Range(1f, 2f) * factor;
                break;
            case Kind.IceWeapon:
                hit.m_damage.m_frost += level / 6f * UnityEngine.Random.Range(0.5f, 1.5f) * factor;
                break;
            case Kind.ThunderWeapon:
                hit.m_damage.m_lightning += level / 8f * UnityEngine.Random.Range(0.5f, 1.5f) * factor;
                break;
        }
        player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, VL_Utility.GetFireballSkillGain * 0.03f);
        effect.Stacks = Mathf.Min(100, effect.Stacks + 1);
        if (UnityEngine.Random.Range(0f, 100f) > effect.Stacks + 1f + evo / 100f) return;
        effect.Stacks = 0;
        Proc(player, kind, __instance.GetEyePoint(), evo, Abj(player));
    }

    private static void Proc(Player player, Kind kind, Vector3 point, float evo, float abj)
    {
        try
        {
            chaining = true;
            var level = LevelSystem.Instance.getLevel();
            var friends = new List<Character>();
            Character.GetCharactersInRange(player.transform.position, 30f, friends);
            foreach (var friend in friends)
            {
                if (friend == null || BaseAI.IsEnemy(player, friend) && !friend.IsPlayer()) continue;
                switch (kind)
                {
                    case Kind.FlameWeapon:
                        friend.Heal(5f + level * 0.25f * (1f + abj / 150f), true);
                        Effect("vfx_Potion_health_medium", friend.GetCenterPoint());
                        break;
                    case Kind.IceWeapon:
                        friend.AddEitr(10f + level * 0.5f * (1f + abj / 75f));
                        friend.GetSEMan().RemoveStatusEffect("Burning".GetStableHashCode(), false);
                        break;
                    case Kind.ThunderWeapon: friend.AddStamina(10f + level * 0.5f * (1f + abj / 75f)); break;
                }
                if (friend.IsPlayer())
                    for (var slot = 1; slot <= 3; slot++)
                        if (friend.GetSEMan().GetStatusEffect(("SE_VL_Ability" + slot + "_CD").GetStableHashCode()) is { } cooldown)
                            cooldown.m_ttl *= 0.9f;
            }
            var targets = new List<Character>();
            Character.GetCharactersInRange(point, kind == Kind.ThunderWeapon ? 30f : 15f, targets);
            var chain = 0.7f;
            foreach (var target in targets)
            {
                if (target == null || !BaseAI.IsEnemy(player, target) || !VL_Utility.LOS_IsValid(target, point)) continue;
                var hit = new HitData();
                var amount = (10f + level * UnityEngine.Random.Range(0.8f, 1.8f) * (1f + evo / 150f)) *
                             VL_GlobalConfigs.g_DamageModifer;
                switch (kind)
                {
                    case Kind.FlameWeapon: hit.m_damage.m_fire = amount; break;
                    case Kind.IceWeapon: hit.m_damage.m_frost = amount; break;
                    case Kind.ThunderWeapon: hit.m_damage.m_lightning = amount * chain; chain *= 0.7f; break;
                }
                hit.m_point = target.GetEyePoint();
                hit.m_skill = ValheimLegends.ValheimLegends.EvocationSkill;
                hit.SetAttacker(player);
                target.Damage(hit);
                Effect(kind == Kind.FlameWeapon ? "fx_CinderFire_Burn" :
                    kind == Kind.IceWeapon ? "fx_DvergerMage_Ice_hit" : "fx_chainlightning_hit",
                    target.transform.position);
                if (kind == Kind.IceWeapon)
                {
                    var slow = ScriptableObject.CreateInstance<SE_Slow>();
                    slow.m_ttl = 4f + 6f * evo;
                    slow.speedAmount = 0.01f;
                    target.GetSEMan().AddStatusEffect(slow);
                }
            }
            Effect(kind == Kind.FlameWeapon ? "fx_fireball_staff_explosion" :
                kind == Kind.ThunderWeapon ? "fx_eikthyr_stomp" : "fx_Potion_frostresist", point);
        }
        finally { chaining = false; }
    }
}

internal sealed class PerspexEnchantment : StatusEffect
{
    internal LegendsEnchanterImbuePatch.Kind Kind;
    internal int Stacks;
    private float timer;

    public override void UpdateStatusEffect(float dt)
    {
        base.UpdateStatusEffect(dt);
        if (m_character is not Player player || player != Player.m_localPlayer) return;
        if ((int)Kind <= 2)
        {
            var label = Kind == LegendsEnchanterImbuePatch.Kind.FlameWeapon ? "Flame" :
                Kind == LegendsEnchanterImbuePatch.Kind.IceWeapon ? "Ice" : "Thunder";
            m_name = label + " Weapon: " + (Stacks * (1f +
                player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.EvocationSkill) / 100f)).ToString("#.#") + "%";
        }
        else if (Kind == LegendsEnchanterImbuePatch.Kind.FlameArmor)
        {
            timer += dt;
            if (timer < 5f) return;
            timer = 0f;
            var level = LevelSystem.Instance.getLevel();
            var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill);
            player.Heal((3f + 0.3f * level * 10f / 6f * (1f + skill / 150f)) *
                        VL_GlobalConfigs.g_DamageModifer * (1f - player.GetHealthPercentage()));
            var heal = ZNetScene.instance?.GetPrefab("vfx_Potion_health_medium");
            if (heal != null) UnityEngine.Object.Instantiate(heal, player.GetCenterPoint(), Quaternion.identity);
        }
    }

    public override void OnDamaged(HitData hit, Character character)
    {
        var player = m_character as Player;
        if (player != null && hit != null)
        {
            var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill);
            var modifier = Mathf.Max(0f, 0.95f - skill * 0.001f);
            if (Kind == LegendsEnchanterImbuePatch.Kind.FlameArmor)
            {
                hit.m_damage.m_spirit *= modifier; hit.m_damage.m_poison *= modifier;
                hit.m_damage.m_fire *= modifier; hit.m_damage.m_frost *= modifier;
                hit.m_damage.m_lightning *= modifier;
                if (hit.HaveAttacker() && !hit.m_ranged && player == Player.m_localPlayer)
                    LegendsEnchanterImbuePatch.ArmorCharge(player, Kind, player.GetEyePoint());
            }
            else if (Kind == LegendsEnchanterImbuePatch.Kind.IceArmor)
            {
                hit.m_damage.m_blunt *= modifier; hit.m_damage.m_pierce *= modifier;
                hit.m_damage.m_slash *= modifier;
                if (hit.GetAttacker() is { } attacker && !hit.m_ranged)
                {
                    var slow = ScriptableObject.CreateInstance<SE_Slow>();
                    slow.m_ttl = 4f + 6f * skill / 150f;
                    slow.speedAmount = 0.7f - skill / 250f;
                    attacker.GetSEMan().AddStatusEffect(slow);
                    var frost = ZNetScene.instance?.GetPrefab("fx_DvergerMage_Ice_hit");
                    if (frost != null) UnityEngine.Object.Instantiate(frost, attacker.transform.position, Quaternion.identity);
                }
                if (hit.HaveAttacker() && !hit.m_ranged && player == Player.m_localPlayer)
                    LegendsEnchanterImbuePatch.ArmorCharge(player, Kind, player.GetEyePoint());
                player.GetSEMan().RemoveStatusEffect("Burning".GetStableHashCode(), false);
            }
            else if (Kind == LegendsEnchanterImbuePatch.Kind.ThunderArmor &&
                     player == Player.m_localPlayer && hit.GetAttacker() is { } attacker)
            {
                LegendsEnchanterImbuePatch.ArmorCharge(player, Kind, player.GetEyePoint());
                var chance = Mathf.Clamp(LevelSystem.Instance.getLevel() /
                    Mathf.Max(1f, hit.GetTotalDamage()), hit.m_ranged ? 0.2f : 0.05f,
                    hit.m_ranged ? 0.6f : 0.15f);
                if (UnityEngine.Random.value <= chance)
                {
                    if (hit.m_ranged) hit.ApplyModifier(0f);
                    else attacker.Stagger(-hit.m_dir);
                    var lightning = ZNetScene.instance?.GetPrefab("fx_chainlightning_spread");
                    if (lightning != null) UnityEngine.Object.Instantiate(lightning, player.GetEyePoint(),
                        Quaternion.LookRotation(attacker.transform.position - player.GetEyePoint()));
                    if (!hit.m_ranged)
                    {
                        lightning = ZNetScene.instance?.GetPrefab("fx_chainlightning_hit");
                        if (lightning != null) UnityEngine.Object.Instantiate(lightning, attacker.GetEyePoint(), Quaternion.identity);
                    }
                    player.RaiseSkill(ValheimLegends.ValheimLegends.AbjurationSkill,
                        VL_Utility.GetFireballSkillGain * 0.015f);
                }
            }
        }
        base.OnDamaged(hit, character);
    }

    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        if (Kind == LegendsEnchanterImbuePatch.Kind.ThunderArmor && m_character is Player player)
            speed *= 1.05f + player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill) * 0.001f;
        base.ModifySpeed(baseSpeed, ref speed, character, dir);
    }

    public override bool IsDone() => ValheimLegends.ValheimLegends.vl_player?.vl_class !=
        ValheimLegends.ValheimLegends.PlayerClass.Enchanter;
    public override bool CanAdd(Character character) => character is Player &&
        ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Enchanter;
}
