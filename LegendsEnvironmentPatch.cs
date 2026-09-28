using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EpicMMOSystem;
using UnityEngine;

namespace PerspexModpackPatcher;

internal static class LegendsEnvironmentPatch
{
    private static bool reflecting;
    private sealed class BiomeTimer { internal float Seconds; internal Light Light; }
    private sealed class BiomeCaster { internal Player Player; }
    private static readonly ConditionalWeakTable<StatusEffect, BiomeTimer> Timers = new();
    private static readonly ConditionalWeakTable<StatusEffect, BiomeCaster> Casters = new();
    private static readonly string[] OtherBiomes = { "Swamp", "Mountain", "Plains", "Ocean", "Mist", "Ash" };
    internal static void Install(Harmony harmony)
    {
        var carry = AccessTools.Method(typeof(Player), "GetMaxCarryWeight");
        var old = typeof(ValheimLegends.ValheimLegends).GetNestedType("PlayerCarryWeight_BiomePatch",
            BindingFlags.Public | BindingFlags.NonPublic);
        if (carry == null || old == null) throw new MissingMethodException("Dekas 0.7.10 biome carry patch missing");
        foreach (var patch in Harmony.GetPatchInfo(carry)?.Postfixes?.ToArray() ?? Array.Empty<Patch>())
            if (patch.PatchMethod.DeclaringType == old)
                harmony.Unpatch(carry, patch.PatchMethod);
        harmony.Patch(carry, postfix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(Carry)));
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.SE_BiomeMeadows), "UpdateStatusEffect"),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(MeadowsInit)),
            transpiler: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(MeadowsTick)));
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.SE_BiomeBlackForest), "UpdateStatusEffect"),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(BlackForestInit)));
        foreach (var biome in OtherBiomes)
        {
            var type = typeof(ValheimLegends.SE_BiomeMeadows).Assembly.GetType("ValheimLegends.SE_Biome" + biome);
            if (type == null) throw new MissingMemberException("Dekas biome missing: " + biome);
            harmony.Patch(AccessTools.Method(type, "UpdateStatusEffect"),
                prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(OtherBiomeInit)),
                postfix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(OtherBiomeTick)));
        }
        foreach (var biome in new[] { "Plains", "Ocean", "Mist" })
            harmony.Patch(AccessTools.Method(typeof(ValheimLegends.SE_BiomeMeadows).Assembly.GetType(
                    "ValheimLegends.SE_Biome" + biome), "OnDamaged"),
                prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(BiomeResistance)));
        harmony.Patch(AccessTools.Method(typeof(SEMan), "AddStatusEffect",
                new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) }),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(InitializeBiome)),
            postfix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(TrackBiomeCaster)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(BiomeHit)) { priority = Priority.Last });
        harmony.Patch(AccessTools.Method(typeof(Player), "UpdateDodge"),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(BeforeDodge)),
            postfix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(AfterDodge)));
        harmony.Patch(AccessTools.Method(typeof(ItemDrop.ItemData), "GetBaseBlockPower", new[] { typeof(int) }),
            postfix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(OceanBlock)));
        harmony.Patch(AccessTools.Method(typeof(StatusEffect), "OnDestroy"),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(RemoveBiomeLight)));
        harmony.Patch(AccessTools.Method(typeof(EnvMan), nameof(EnvMan.IsCold)),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(Cold)) { priority = Priority.First });
        harmony.Patch(AccessTools.Method(typeof(EnvMan), nameof(EnvMan.IsWet)),
            prefix: new HarmonyMethod(typeof(LegendsEnvironmentPatch), nameof(Wet)) { priority = Priority.First });
    }

    private static void InitializeBiome(StatusEffect __0)
    {
        if (__0 is ValheimLegends.SE_BiomeMeadows meadows) meadows.doOnce = true;
        if (__0 is ValheimLegends.SE_BiomeBlackForest forest) forest.doOnce = true;
        if (__0 != null && OtherBiomes.Any(name => __0.name == "SE_VL_Biome" + name))
            Traverse.Create(__0).Field("doOnce").SetValue(true);
    }

    private static void TrackBiomeCaster(StatusEffect __0, StatusEffect __result)
    {
        if (__0?.name == null || !__0.name.StartsWith("SE_VL_Biome", StringComparison.Ordinal) ||
            __result == null || Player.m_localPlayer == null ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Enchanter)
            return;
        Casters.GetValue(__result, _ => new BiomeCaster()).Player = Player.m_localPlayer;
    }

    private static void RemoveOtherBiomes(StatusEffect current, Character character)
    {
        if (!Casters.TryGetValue(current, out var caster) || caster.Player == null) return;
        foreach (var other in character.GetSEMan().GetStatusEffects().ToArray())
            if (other != current && other?.name?.StartsWith("SE_VL_Biome", StringComparison.Ordinal) == true &&
                Casters.TryGetValue(other, out var previous) && previous.Player == caster.Player)
                character.GetSEMan().RemoveStatusEffect(other, false);
    }

    private static (float level, float power, float scale) BiomeScale(Character character, string biome)
    {
        if (character is Player player)
        {
            var level = LevelSystem.Instance;
            var power = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill) *
                        (1f + Mathf.Clamp(level.getAddHp() / 400f + level.getAddStamina() / 200f, 0f, 0.5f));
            var scale = biome == "Mountain" ? level.getLevel() * 10f / 6f *
                (1f + Mathf.Clamp(level.getAddHp() / 400f + level.getAddMagicDamage() / 80f, 0f, 0.5f)) :
                level.getLevel() * 10f / 6f * (1f + power / 300f);
            return (level.getLevel(), power, scale);
        }
        var fallback = biome switch
        {
            "Mountain" => (40f, 60f), "Plains" => (50f, 80f), "Mist" => (50f, 100f),
            "Ash" => (60f, 120f), _ => (30f, 40f)
        };
        return (fallback.Item1, fallback.Item2,
            fallback.Item1 * 10f / 6f * (1f + fallback.Item2 / 300f));
    }

    private static void OtherBiomeInit(StatusEffect __instance)
    {
        var state = Traverse.Create(__instance);
        if (!state.Field("doOnce").GetValue<bool>()) return;
        var character = state.Field("m_character").GetValue<Character>();
        if (character == null) return;
        var biome = __instance.name.Substring("SE_VL_Biome".Length);
        var (level, power, scale) = BiomeScale(character, biome);
        RemoveOtherBiomes(__instance, character);
        state.Field("doOnce").SetValue(false);
        __instance.m_name = "Biome: " + biome;
        __instance.m_ttl = 600f + (biome == "Mist" || biome == "Ash" ? 5f : 3f) * scale;
        var resist = (biome == "Plains" || biome == "Ocean" ? 0.9f : 0.85f) - 0.001f * scale;
        state.Field("resistModifier").SetValue(resist);
        if (biome == "Mountain") state.Field("staminaRegen").SetValue(5f + 0.075f * scale);
        if (biome == "Plains") state.Field("speedBonus").SetValue(1.05f + 0.001f * scale);
        if (biome == "Ocean") state.Field("swimSpeed").SetValue(1.5f + 0.01f * scale);
        if (biome == "Mist") state.Field("iceDamageOffset").SetValue(0f);
        if (biome == "Ash") state.Field("fireDamageOffset").SetValue(0f);
        var damage = (5f + level / 2f * (1f + power / 300f)) *
                     ValheimLegends.VL_GlobalConfigs.g_DamageModifer;
        __instance.m_tooltip = biome switch
        {
            "Swamp" => $"Adds up to {damage:0.#} Poison damage. Poison resistance. Keeps you dry.",
            "Mountain" => $"Adds up to {damage:0.#} Frost damage. Frost resistance. Cold immunity.",
            "Plains" => "Faster movement, cheaper dodges and a chance to reduce ability cooldowns on hit.",
            "Ocean" => $"Adds up to {damage:0.#} Spirit damage. Physical resistance, block and swim speed.",
            "Mist" => $"Adds up to {damage:0.#} Lightning damage. Lightning resistance and Eitr regeneration.",
            _ => $"Adds up to {damage:0.#} Fire damage. Fire resistance and light."
        };
    }

    private static void OtherBiomeTick(StatusEffect __instance, float dt)
    {
        if (__instance.name == "SE_VL_BiomeAsh")
        {
            var state = Timers.GetValue(__instance, _ => new BiomeTimer());
            var ashOwner = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
            if (ashOwner != null && state.Light == null)
            {
                state.Light = ashOwner.gameObject.AddComponent<Light>();
                state.Light.range = 30f;
                state.Light.color = new Color(233f, 240f, 226f);
                state.Light.intensity = 0.0035f;
            }
            return;
        }
        if (__instance.name != "SE_VL_BiomeMist") return;
        var timer = Timers.GetValue(__instance, _ => new BiomeTimer());
        timer.Seconds -= dt;
        if (timer.Seconds > 0f) return;
        timer.Seconds = 5f;
        var character = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (character == null) return;
        var amount = 5f + 0.075f * BiomeScale(character, "Mist").scale;
        if (character.HaveEitr(1f)) character.AddEitr(amount);
        else character.AddStamina(amount);
    }

    private static void RemoveBiomeLight(StatusEffect __instance)
    {
        if (__instance.name == "SE_VL_BiomeAsh" && Timers.TryGetValue(__instance, out var state) && state.Light != null)
            UnityEngine.Object.Destroy(state.Light);
    }

    private static bool BiomeResistance(StatusEffect __instance, HitData hit)
    {
        if (hit == null) return false;
        var biome = __instance.name.Substring("SE_VL_Biome".Length);
        if (biome == "Plains") return false;
        var resistance = Traverse.Create(__instance).Field("resistModifier").GetValue<float>();
        if (biome == "Ocean")
        {
            hit.m_damage.m_blunt *= resistance;
            hit.m_damage.m_pierce *= resistance;
            hit.m_damage.m_slash *= resistance;
        }
        else if (biome == "Mist") hit.m_damage.m_lightning *= resistance;
        return false;
    }

    private static void BeforeDodge(Player __instance, ref float __state)
    {
        __state = __instance.m_dodgeStaminaUsage;
        if (__instance.GetSEMan().HaveStatusEffect("SE_VL_BiomePlains".GetStableHashCode()))
            __instance.m_dodgeStaminaUsage *= 0.8f - 0.008f * BiomeScale(__instance, "Plains").scale;
    }

    private static void AfterDodge(Player __instance, float __state) => __instance.m_dodgeStaminaUsage = __state;

    private static void OceanBlock(ref float __result)
    {
        var player = Player.m_localPlayer;
        if (player?.GetSEMan()?.HaveStatusEffect("SE_VL_BiomeOcean".GetStableHashCode()) == true)
            __result *= 0.2f + 0.002f * BiomeScale(player, "Ocean").scale;
    }

    private static float MeadowsScale(Character character)
    {
        if (character is not Player player) return 5f * 10f / 6f * (1f + 10f / 300f);
        var level = LevelSystem.Instance;
        var abjuration = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AbjurationSkill) *
                         (1f + Mathf.Clamp(level.getAddHp() / 400f + level.getAddStamina() / 200f, 0f, 0.5f));
        return level.getLevel() * 10f / 6f * (1f + abjuration / 300f);
    }

    private static float ForestScale(Character character)
    {
        if (character is not Player player) return 15f * 10f / 6f * (1f + 20f / 300f);
        return MeadowsScale(player);
    }

    private static void BlackForestInit(ValheimLegends.SE_BiomeBlackForest __instance)
    {
        if (!__instance.doOnce) return;
        var character = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (character == null) return;
        var scale = ForestScale(character);
        RemoveOtherBiomes(__instance, character);
        __instance.doOnce = false;
        __instance.m_ttl = ValheimLegends.SE_BiomeBlackForest.m_baseTTL + 3f * scale;
        __instance.m_name = "Biome: Black Forest";
        __instance.m_tooltip = $"Shelter anywhere. {0.02f + 0.0004f * scale:P1} chance to crit or steal coins. " +
            $"Reflects {0.2f + 0.002f * scale:P1} of melee damage.";
    }

    private static void MeadowsInit(ValheimLegends.SE_BiomeMeadows __instance)
    {
        if (!__instance.doOnce) return;
        var character = Traverse.Create(__instance).Field("m_character").GetValue<Character>();
        if (character == null) return;
        var amount = MeadowsScale(character);
        RemoveOtherBiomes(__instance, character);
        __instance.doOnce = false;
        __instance.m_ttl = ValheimLegends.SE_BiomeMeadows.m_baseTTL + 3f * amount;
        __instance.regenBonus = (3f + 0.3f * amount) * ValheimLegends.VL_GlobalConfigs.g_DamageModifer;
        __instance.m_name = "Biome: Meadows";
        __instance.m_tooltip = $"Increases Carry Weight by {50f + amount:0}\n" +
            $"Regenerates {__instance.regenBonus:0.#} Health scaled by current Health every 5 seconds\n" +
            "Heals 5% of damage dealt scaled by current Health.";
    }

    private static IEnumerable<CodeInstruction> MeadowsTick(IEnumerable<CodeInstruction> source)
    {
        var heal = 0;
        var stamina = 0;
        foreach (var instruction in source)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character))
            {
                if (method.Name == nameof(Character.Heal))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegendsEnvironmentPatch), nameof(HealMeadows));
                    heal++;
                }
                else if (method.Name == nameof(Character.AddStamina))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(LegendsEnvironmentPatch), nameof(IgnoreStamina));
                    stamina++;
                }
            }
            yield return instruction;
        }
        if (heal != 1 || stamina != 1)
            throw new InvalidOperationException($"Unexpected Meadows tick sites: heal={heal}, stamina={stamina}");
    }

    private static void HealMeadows(Character character, float amount, bool showText) =>
        character.Heal(amount * character.GetHealthPercentage(), showText);

    private static void IgnoreStamina(Character character, float amount) { }

    private static void BiomeHit(Character __instance, HitData hit)
    {
        if (reflecting || __instance == null || hit == null || hit.GetTotalDamage() <= 0f) return;
        var attacker = hit.GetAttacker();
        if (attacker == null) return;
        if (!hit.m_ranged && __instance.GetSEMan().HaveStatusEffect("SE_VL_BiomeBlackForest".GetStableHashCode()))
        {
            var reflected = new HitData { m_damage = hit.m_damage, m_dir = -hit.m_dir,
                m_point = attacker.GetCenterPoint() };
            reflected.SetAttacker(__instance);
            reflected.ApplyModifier(0.2f + 0.002f * ForestScale(__instance));
            try { reflecting = true; attacker.Damage(reflected); }
            finally { reflecting = false; }
        }
        var player = attacker as Player;
        if (attacker.GetSEMan().HaveStatusEffect("SE_VL_BiomeMeadows".GetStableHashCode()))
        {
            var percent = 0.02f + 0.0002f * MeadowsScale(attacker);
            attacker.Heal(hit.GetTotalDamage() * attacker.GetHealthPercentage() * percent *
                        UnityEngine.Random.Range(0.8f, 1.2f));
        }
        if (attacker.GetSEMan().HaveStatusEffect("SE_VL_BiomeBlackForest".GetStableHashCode()))
        {
            var chance = 0.02f + 0.0004f * ForestScale(attacker);
            if (UnityEngine.Random.value < chance)
            {
                hit.ApplyModifier(hit.m_backstabBonus);
                player?.Message(MessageHud.MessageType.TopLeft, $"Critical! ({hit.m_backstabBonus:0.#}x damage)");
            }
            else if (player != null && player == Player.m_localPlayer && !hit.m_ranged && UnityEngine.Random.value < chance)
            {
                var coins = Mathf.CeilToInt(UnityEngine.Random.Range(0.2f, 0.5f) *
                    Mathf.Sqrt(__instance.GetMaxHealth()));
                if (LegendsDuelistChallengePatch.AwardCoins(player, coins))
                    player.Message(MessageHud.MessageType.TopLeft, $"Snatched {coins} coins from {__instance.GetHoverName()}!");
            }
        }
        foreach (var biome in OtherBiomes)
        {
            if (!attacker.GetSEMan().HaveStatusEffect(("SE_VL_Biome" + biome).GetStableHashCode())) continue;
            var (level, power, scale) = BiomeScale(attacker, biome);
            if (biome == "Plains")
            {
                if (player == null) continue;
                foreach (var name in new[] { "SE_VL_Ability1_CD", "SE_VL_Ability2_CD", "SE_VL_Ability3_CD",
                             "SE_VL_DyingLight_CD", "SE_VL_CDReactivearmor" })
                    if (player.GetSEMan().GetStatusEffect(name.GetStableHashCode()) is { } cooldown &&
                        UnityEngine.Random.value < 0.25f + scale / 300f)
                        cooldown.m_ttl -= 1f;
                continue;
            }
            var amount = (5f + level / 2f * (1f + power / 300f)) *
                         ValheimLegends.VL_GlobalConfigs.g_DamageModifer;
            var cap = hit.GetTotalDamage() * 0.5f;
            switch (biome)
            {
                case "Swamp": hit.m_damage.m_poison += Mathf.Clamp(amount * UnityEngine.Random.Range(0.8f, 1.2f), 0f, cap); break;
                case "Mountain": hit.m_damage.m_frost += Mathf.Clamp(amount * UnityEngine.Random.Range(0.9f, 1.1f), 0f, cap); break;
                case "Ocean": hit.m_damage.m_spirit += Mathf.Clamp(amount * UnityEngine.Random.Range(0.5f, 2f), 0f, cap); break;
                case "Mist": hit.m_damage.m_lightning += Mathf.Clamp(amount * UnityEngine.Random.Range(0.25f, 1.75f), 0f, cap); break;
                case "Ash": hit.m_damage.m_fire += Mathf.Clamp(amount * UnityEngine.Random.Range(0.5f, 1.5f), 0f, cap); break;
            }
        }
    }

    private static void Carry(Player __instance, ref float __result)
    {
        if (__instance?.GetSEMan()?.HaveStatusEffect("SE_VL_BiomeMeadows".GetStableHashCode()) != true) return;
        __result += 50f + MeadowsScale(__instance);
    }

    private static bool Cold(ref bool __result)
    {
        var player = Player.m_localPlayer;
        if (player == null) return true;
        if (player.GetSEMan().HaveStatusEffect("SE_VL_BiomeMountain".GetStableHashCode()) ||
            player.GetSEMan().HaveStatusEffect("SE_VL_FlameArmor".GetStableHashCode()) ||
            LegendsMageAffinityPatch.Get(player, LegendsMageAffinityPatch.Focus.Fire)?.Focused == true)
        {
            __result = false;
            return false;
        }
        return true;
    }

    private static bool Wet(ref bool __result)
    {
        if (Player.m_localPlayer?.GetSEMan()?.HaveStatusEffect("SE_VL_BiomeSwamp".GetStableHashCode()) == true)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
