using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsDruidRootsPatch
{
    private static readonly int Cooldown = "SE_VL_Ability3_CD".GetStableHashCode();
    private static readonly int Form = LegendsFenringPatch.FormName.GetStableHashCode();
    private static readonly int AimMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece_nonsolid", "terrain", "vehicle", "piece", "viewblock", "character", "character_net", "character_ghost");
    private static Player owner;
    private static Projectile staged;
    private static float counter;
    private static int total;
    private static int interval;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(Class_Druid), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsDruidRootsPatch), nameof(Input)) { priority = Priority.High });
    }

    internal static void Sync()
    {
        if (owner == null) return;
        if (owner != Player.m_localPlayer || owner.IsDead() ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Druid ||
            owner.GetSEMan().HaveStatusEffect(Form))
        {
            if (staged != null) UnityEngine.Object.Destroy(staged.gameObject);
            owner = null;
            staged = null;
            total = 0;
        }
    }

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer) return true;
        if (ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Druid ||
            player.GetSEMan().HaveStatusEffect(Form))
        {
            if (owner != null) Finish(owner);
            return true;
        }
        if (owner != null && owner != player) Finish(owner);
        if (VL_Utility.Ability3_Input_Down)
        {
            if (owner == null) Start(player);
            return false;
        }
        if (owner == null) return true;
        var cost = VL_Utility.GetRootCost * total * Time.deltaTime;
        if (!VL_Utility.Ability3_Input_Pressed || player.GetStamina() <= cost)
        {
            Finish(player);
            return false;
        }
        counter += Time.deltaTime * 60f;
        VL_Utility.SetTimer();
        player.UseStamina(cost);
        if (counter >= interval)
        {
            player.RaiseSkill(ValheimLegends.ValheimLegends.ConjurationSkill, 0.06f);
            Fire(player, 75f, 2f);
            total++;
            counter = 0f;
            Stage(player, interval + 1f);
        }
        return false;
    }

    private static void Start(Player player)
    {
        if (player.GetSEMan().HaveStatusEffect(Cooldown))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ability not ready.");
            return;
        }
        var cost = VL_Utility.GetRootCost;
        if (player.GetStamina() < cost || ValheimLegends.ValheimLegends.isChanneling)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Not enough stamina to channel Root: ({player.GetStamina():0.#}/{cost:0.#})");
            return;
        }
        if (ZNetScene.instance?.GetPrefab("gdking_root_projectile") == null) return;
        owner = player;
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        // The published isChanneling flag also blocks Player.CanMove; this channel tracks its own state.
        var cooldown = ScriptableObject.CreateInstance<SE_Ability3_CD>();
        cooldown.m_ttl = VL_Utility.GetRootCooldownTime;
        player.GetSEMan().AddStatusEffect(cooldown);
        player.UseStamina(cost);
        player.StartEmote("point");
        var level = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill) *
                    (1f + Mathf.Clamp(LevelSystem.Instance.getAddStamina() / 200f + LevelSystem.Instance.getAddMagicDamage() / 80f, 0f, 0.5f));
        interval = Mathf.Max(1, 16 - Mathf.RoundToInt(0.05f * level - 7.5f * LevelSystem.Instance.getAddMagicDamage() / 80f));
        counter = 0f;
        total = 0;
        Stage(player, 35f);
        player.RaiseSkill(ValheimLegends.ValheimLegends.ConjurationSkill, VL_Utility.GetRootSkillGain);
    }

    private static void Stage(Player player, float ttl)
    {
        var prefab = ZNetScene.instance?.GetPrefab("gdking_root_projectile");
        if (prefab == null) return;
        var side = player.transform.right * (UnityEngine.Random.value < 0.5f ? -2.5f : 2.5f);
        var position = player.transform.position + player.transform.up * 3f + player.GetLookDir() * 2f + side;
        var obj = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        staged = obj.GetComponent<Projectile>();
        if (staged == null) { UnityEngine.Object.Destroy(obj); return; }
        staged.name = "VL_DruidRoot";
        staged.m_respawnItemOnHit = false;
        staged.m_spawnOnHit = null;
        staged.m_ttl = ttl;
        staged.m_gravity = 0f;
        staged.m_rayRadius = 0.1f;
        Traverse.Create(staged).Field("m_skill").SetValue(ValheimLegends.ValheimLegends.ConjurationSkill);
        var look = player.GetLookDir();
        if (look.sqrMagnitude > 0.000001f) staged.transform.localRotation = Quaternion.LookRotation(look);
        obj.transform.localScale = Vector3.one * 1.5f;
    }

    private static void Fire(Player player, float speed, float push)
    {
        if (staged == null) return;
        var origin = staged.transform.position;
        var look = player.GetLookDir();
        var target = Physics.Raycast(player.GetEyePoint(), look, out var ray, float.PositiveInfinity, AimMask) && ray.collider
            ? ray.point : player.transform.position + look * 1000f;
        var rawSkill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.ConjurationSkill);
        var hit = new HitData();
        hit.m_damage.m_pierce = LegendsEconomyPatch.Magic(rawSkill,
            LevelSystem.Instance.getParameter(Parameter.Body), 0.35f, VL_GlobalConfigs.c_druidVines);
        hit.m_pushForce = push;
        hit.m_skill = ValheimLegends.ValheimLegends.ConjurationSkill;
        hit.SetAttacker(player);
        staged.Setup(player, (Vector3.MoveTowards(origin, target, 1f) - origin) * speed, -1f, hit, null, null);
        staged = null;
    }

    private static void Finish(Player player)
    {
        if (player != null) Fire(player, 65f, 10f);
        owner = null;
        staged = null;
        total = 0;
    }
}
