using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsRogueDamagePatch
{
    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Rogue), "Execute_Throw"),
            transpiler: new HarmonyMethod(typeof(LegendsRogueDamagePatch), nameof(ThrowCalls)));
        harmony.Patch(AccessTools.Method(typeof(Class_Rogue), "Process_Input"),
            transpiler: new HarmonyMethod(typeof(LegendsRogueDamagePatch), nameof(BackstabCall)));
    }

    private static IEnumerable<CodeInstruction> ThrowCalls(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Projectile) &&
                method.Name == nameof(Projectile.Setup))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsRogueDamagePatch),
                    count++ == 0 ? nameof(PoisonBomb) : nameof(ThrowingKnife));
            }
            yield return instruction;
        }
        if (count != 2) throw new InvalidOperationException($"Unexpected Rogue projectile sites: {count}");
    }

    private static IEnumerable<CodeInstruction> BackstabCall(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        var ability = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo getter && getter == AccessTools.PropertyGetter(typeof(VL_Utility), "Ability1_Input_Down"))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsRogueDamagePatch), nameof(SmokeOrPoison));
                ability++;
            }
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Character) &&
                method.Name == nameof(Character.Damage))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(LegendsRogueDamagePatch), nameof(Backstab));
                count++;
            }
            yield return instruction;
        }
        if (count != 1) throw new InvalidOperationException($"Unexpected Rogue backstab sites: {count}");
        if (ability != 1) throw new InvalidOperationException($"Unexpected Rogue Ability 1 sites: {ability}");
    }

    private static bool SmokeOrPoison()
    {
        if (!VL_Utility.Ability1_Input_Down) return false;
        var player = Player.m_localPlayer;
        if (player == null || !player.IsCrouching()) return true;

        var effects = player.GetSEMan();
        if (effects.HaveStatusEffect("SE_VL_Ability1_CD".GetStableHashCode()))
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready");
        else if (player.GetStamina() < VL_Utility.GetFadeCost)
            player.Message(MessageHud.MessageType.TopLeft,
                $"Not enough stamina for Smoke Bomb: ({player.GetStamina():#.#}/{VL_Utility.GetFadeCost:#.#})");
        else
        {
            var cooldown = ScriptableObject.CreateInstance<SE_Ability1_CD>();
            cooldown.m_ttl = VL_Utility.GetPoisonBombCooldownTime;
            effects.AddStatusEffect(cooldown);
            player.UseStamina(VL_Utility.GetFadeCost);
            UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("fx_VL_Smokeburst"),
                player.transform.position, Quaternion.identity);
            UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab("fx_VL_Shadowburst"),
                player.transform.position + player.transform.up * 0.5f,
                Quaternion.LookRotation(player.GetLookDir()));
            var nearby = new List<Character>();
            Character.GetCharactersInRange(player.GetCenterPoint(), 500f, nearby);
            foreach (var target in nearby)
            {
                if (target.GetBaseAI() is not MonsterAI monster || !monster.IsEnemy(player) ||
                    monster.GetTargetCreature() != player) continue;
                Traverse.Create(monster).Field("m_alerted").SetValue(false);
                Traverse.Create(monster).Field("m_targetCreature").SetValue(null);
            }
            player.Message(MessageHud.MessageType.Center, "Smoke Bomb!");
            player.RaiseSkill(ValheimLegends.ValheimLegends.IllusionSkill, VL_Utility.GetFadeSkillGain);
        }
        return false;
    }

    private static void PoisonBomb(Projectile projectile, Character owner, Vector3 velocity,
        float noise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        var damage = LegendsEconomyPatch.Physical(caster, school,
            LevelSystem.Instance.getParameter(Parameter.Special), 0.20f, VL_GlobalConfigs.c_roguePoisonBomb);
        hit.m_damage.m_poison = damage.GetTotalDamage();
        hit.m_skill = ValheimLegends.ValheimLegends.AlterationSkill;
        hit.SetAttacker(caster);
        projectile.m_projectilesInheritHitData = true;
        projectile.m_onlySpawnedProjectilesDealDamage = true;
        var area = projectile.m_spawnOnHit?.GetComponentInChildren<Aoe>();
        if (area != null)
        {
            area.m_useAttackSettings = true;
            area.m_damage.m_poison = 0f;
        }
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void ThrowingKnife(Projectile projectile, Character owner, Vector3 velocity,
        float noise, HitData hit, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
    {
        var caster = (Player)owner;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        hit.m_damage = LegendsEconomyPatch.Physical(caster, school,
            LevelSystem.Instance.getParameter(Parameter.Agility), 0.60f, VL_GlobalConfigs.c_rogueBonusThrowingDagger);
        hit.m_skill = ValheimLegends.ValheimLegends.DisciplineSkill;
        hit.SetAttacker(caster);
        projectile.Setup(owner, velocity, noise, hit, item, ammo);
    }

    private static void Backstab(Character target, HitData hit)
    {
        var caster = Player.m_localPlayer;
        var school = caster.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        hit.m_damage = LegendsEconomyPatch.Physical(caster, school,
            LevelSystem.Instance.getParameter(Parameter.Special), 1.60f, VL_GlobalConfigs.c_rogueBackstab);
        hit.SetAttacker(caster);
        target.Damage(hit);
    }
}
