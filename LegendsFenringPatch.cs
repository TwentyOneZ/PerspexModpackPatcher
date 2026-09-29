using System;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsFenringPatch
{
    internal const string FormName = "SE_VL_DruidFenringForm";
    private const string CooldownName = "SE_VL_Shapeshift_CD";
    private static readonly int FormHash = FormName.GetStableHashCode();
    private static readonly int CooldownHash = CooldownName.GetStableHashCode();
    private static bool canDoubleJump = true;

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ObjectDB), "Awake"),
            postfix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(Register)));
        var copy = AccessTools.Method(typeof(ObjectDB), "CopyOtherDB");
        if (copy != null) harmony.Patch(copy, postfix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(Register)));
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        harmony.Patch(AccessTools.Method(typeof(Class_Druid), "Process_Input"),
            prefix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(Input)) { priority = Priority.First });
        var update = AccessTools.Method(typeof(ValheimLegends.ValheimLegends)
            .GetNestedType("AbilityInput_Postfix", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic), "Postfix");
        if (update == null) throw new MissingMethodException("Dekas 0.7.10 ability update missing");
        harmony.Patch(update, postfix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(FinishDash)));
        harmony.Patch(AccessTools.Method(typeof(Character), nameof(Character.Damage), new[] { typeof(HitData) }),
            prefix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(UnarmedDamage)));
        harmony.Patch(AccessTools.Method(typeof(ItemDrop.ItemData), "GetBaseBlockPower", new[] { typeof(int) }),
            postfix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(UnarmedBlock)));
        harmony.Patch(AccessTools.Method(typeof(ValheimLegends.ValheimLegends), "NameCooldowns"),
            postfix: new HarmonyMethod(typeof(LegendsFenringPatch), nameof(NameAbilities)));
    }

    private static void Register(ObjectDB __instance)
    {
        if (__instance.m_StatusEffects == null) return;
        if (!__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == FormName))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexFenringForm>());
        if (!__instance.m_StatusEffects.Exists(effect => effect != null && effect.name == CooldownName))
            __instance.m_StatusEffects.Add(ScriptableObject.CreateInstance<PerspexShapeshiftCooldown>());
    }

    private static bool Input(Player player)
    {
        if (player != Player.m_localPlayer || ValheimLegends.ValheimLegends.vl_player?.vl_class !=
            ValheimLegends.ValheimLegends.PlayerClass.Druid) return true;
        var effects = player.GetSEMan();
        var transformed = effects.HaveStatusEffect(FormHash);
        if (!transformed && !(player.IsBlocking() && VL_Utility.Ability2_Input_Down)) return true;
        if (!transformed)
        {
            ChangeForm(player, false);
            return false;
        }
        if (player.IsOnGround()) canDoubleJump = true;
        if (ZInput.GetButtonDown("Jump") && !player.IsOnGround() && canDoubleJump && !player.IsDead() &&
            !player.InAttack() && !player.IsEncumbered() && !player.InDodge() && !player.IsKnockedBack())
        {
            var body = Traverse.Create(player).Field("m_body").GetValue<Rigidbody>();
            if (body != null)
            {
                var velocity = player.GetVelocity();
                velocity.y = 0f;
                body.linearVelocity = velocity * 2f + Vector3.up * 8f;
                canDoubleJump = false;
                var animation = Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>();
                animation?.SetTrigger("jump");
            }
        }
        if (VL_Utility.Ability2_Input_Down && player.IsBlocking()) ChangeForm(player, false);
        else if (VL_Utility.Ability1_Input_Down) Class_Ranger.Process_Input(player);
        else if (VL_Utility.Ability2_Input_Down) Class_Valkyrie.Process_Input(player);
        else if (VL_Utility.Ability3_Input_Down) Class_Berserker.Process_Input(player, ref unusedAltitude);
        return false;
    }

    private static float unusedAltitude;

    private static void FinishDash(Player __0, ref float __1, ref Rigidbody __2)
    {
        if (__0 != Player.m_localPlayer || __0.GetSEMan() == null || !__0.GetSEMan().HaveStatusEffect(FormHash) ||
            !ValheimLegends.ValheimLegends.isChargingDash || ValheimLegends.ValheimLegends.dashCounter < 12) return;
        ValheimLegends.ValheimLegends.isChargingDash = false;
        Class_Berserker.Execute_Dash(__0, ref __1, ref __2);
    }

    private static void UnarmedDamage(HitData hit)
    {
        if (hit == null || hit.m_damage.m_blunt <= 0f || hit.GetAttacker() is not Player player ||
            player != Player.m_localPlayer || !player.GetSEMan().HaveStatusEffect(FormHash)) return;
        var weapon = player.GetCurrentWeapon();
        if (weapon?.m_shared?.m_name != "Unarmed" || player.GetInventory().ContainsItem(weapon)) return;
        var level = LevelSystem.Instance;
        var unarmed = player.GetSkills().GetSkillLevel(Skills.SkillType.Unarmed);
        var discipline = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        var baseDamage = (4f + 0.9f * level.getLevel()) *
            (0.75f + 0.005f * Mathf.Clamp(unarmed, 0f, 100f)) *
            (0.75f + 0.005f * Mathf.Clamp(discipline, 0f, 100f)) *
            (1f + level.getAddPhysicDamage() / 100f);
        hit.m_damage = new HitData.DamageTypes { m_blunt = baseDamage * 0.4f, m_slash = baseDamage * 0.4f };
        player.RaiseSkill(ValheimLegends.ValheimLegends.DisciplineSkill,
            0.001f * VL_GlobalConfigs.g_SkillGainModifer * (1f + level.getAddMagicDamage() / 16f));
    }

    private static void UnarmedBlock(ItemDrop.ItemData __instance, ref float __result)
    {
        var player = Player.m_localPlayer;
        if (player == null || __instance?.m_shared?.m_name != "Unarmed" ||
            ValheimLegends.ValheimLegends.vl_player?.vl_class != ValheimLegends.ValheimLegends.PlayerClass.Druid ||
            !player.GetSEMan().HaveStatusEffect(FormHash)) return;
        var level = LevelSystem.Instance;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.DisciplineSkill);
        __result += skill * (1f + Mathf.Clamp(level.getAddPhysicDamage() / 40f +
                                            level.getAddAttackSpeed() / 40f, 0f, 0.5f)) *
                    VL_GlobalConfigs.c_monkBonusBlock * 0.25f;
    }

    private static void NameAbilities()
    {
        if (Player.m_localPlayer?.GetSEMan()?.HaveStatusEffect(FormHash) != true) return;
        ValheimLegends.ValheimLegends.Ability1_Name = "Shadow";
        ValheimLegends.ValheimLegends.Ability2_Name = "Stagger";
        ValheimLegends.ValheimLegends.Ability3_Name = "Dash";
    }

    private static void ChangeForm(Player player, bool forced)
    {
        if (ObjectDB.instance != null) Register(ObjectDB.instance);
        var effects = player.GetSEMan();
        var transformed = effects.HaveStatusEffect(FormHash);
        var cost = VL_Utility.GetDefenderCost;
        if (!forced && !transformed && effects.HaveStatusEffect(CooldownHash))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Can't shapeshift again yet.");
            return;
        }
        if (!forced && !transformed && player.GetStamina() < cost)
        {
            player.Message(MessageHud.MessageType.TopLeft, $"Not enough stamina: ({player.GetStamina():0.#}/{cost:0.#})");
            return;
        }
        if (!transformed || forced)
        {
            var cooldown = ScriptableObject.CreateInstance<PerspexShapeshiftCooldown>();
            cooldown.m_ttl = (forced ? 180f : 30f) * VL_GlobalConfigs.g_CooldownModifer *
                             LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Intellect));
            if (forced && effects.HaveStatusEffect(CooldownHash)) effects.RemoveStatusEffect(CooldownHash, false);
            effects.AddStatusEffect(cooldown);
        }
        if (!forced && !transformed) player.UseStamina(cost);
        if (transformed)
        {
            effects.RemoveStatusEffect(FormHash, false);
            ValheimLegends.ValheimLegends.isChargingDash = false;
            canDoubleJump = true;
        }
        else effects.AddStatusEffect(ScriptableObject.CreateInstance<PerspexFenringForm>());
        ValheimLegends.ValheimLegends.NameCooldowns();
        var icons = ValheimLegends.ValheimLegends.abilitiesStatus;
        if (icons != null)
        {
            foreach (var icon in icons)
                if (icon != null) UnityEngine.Object.Destroy(icon.gameObject);
            icons.Clear();
        }
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        var animation = Traverse.Create(player).Field("m_zanim").GetValue<ZSyncAnimation>();
        animation?.SetTrigger("gpower");
        var prefab = ZNetScene.instance?.GetPrefab("vfx_odin_despawn");
        if (prefab != null) UnityEngine.Object.Instantiate(prefab, player.transform.position, Quaternion.identity);
        prefab = ZNetScene.instance?.GetPrefab("sfx_wraith_death");
        if (prefab != null) UnityEngine.Object.Instantiate(prefab, player.transform.position, Quaternion.identity);
    }

    internal static void Exhausted(Player player)
    {
        if (player == Player.m_localPlayer && player.GetSEMan().HaveStatusEffect(FormHash))
        {
            player.Message(MessageHud.MessageType.TopLeft, "Ran out of Eitr to sustain shapeshifting.");
            ChangeForm(player, true);
        }
    }
}

internal sealed class PerspexShapeshiftCooldown : StatusEffect
{
    public PerspexShapeshiftCooldown()
    {
        name = "SE_VL_Shapeshift_CD";
        m_name = "Shapeshift Cooldown";
        var item = ZNetScene.instance?.GetPrefab("TrophyFenring")?.GetComponent<ItemDrop>();
        if (item != null) m_icon = item.m_itemData.GetIcon();
    }
}

internal sealed class PerspexFenringForm : StatusEffect
{
    private static readonly int Helmet = "HelmetCultist".GetStableHashCode();
    private static readonly int Chest = "ArmorCultistChest".GetStableHashCode();
    private static readonly int Legs = "ArmorCultistLegs".GetStableHashCode();
    private static readonly int Shoulder = "CapeCultist".GetStableHashCode();
    private float healTimer;
    private float sustainTimer = 30f;
    private float visualTimer;
    private bool visualApplied;
    private bool initialized;

    public PerspexFenringForm()
    {
        name = LegendsFenringPatch.FormName;
        m_name = "Shapeshift: Fenring";
        m_tooltip = "Increased movement, health and stamina regeneration. Consumes Eitr to sustain.";
        var item = ZNetScene.instance?.GetPrefab("TrophyFenring")?.GetComponent<ItemDrop>();
        if (item != null) m_icon = item.m_itemData.GetIcon();
    }

    public override void Setup(Character character)
    {
        base.Setup(character);
        if (m_icon == null)
        {
            var item = ZNetScene.instance?.GetPrefab("TrophyFenring")?.GetComponent<ItemDrop>();
            if (item != null) m_icon = item.m_itemData.GetIcon();
        }
        ApplyVisual();
    }

    public override void UpdateStatusEffect(float dt)
    {
        if (m_character is not Player player) return;
        var level = LevelSystem.Instance;
        var skill = player.GetSkills().GetSkillLevel(ValheimLegends.ValheimLegends.AlterationSkill);
        var power = skill * (1f + Mathf.Clamp(level.getAddCriticalChance() / 40f + level.getAddMagicDamage() / 80f, 0f, 0.5f));
        if (!initialized) { initialized = true; sustainTimer *= 1f + power / 150f; }
        healTimer -= dt;
        if (healTimer <= 0f)
        {
            healTimer = 5f;
            var strength = 3f + 0.3f * (level.getLevel() * 5f / 6f * (1f + power / 150f));
            player.Heal(strength * (1f - player.GetHealthPercentage()));
            player.AddStamina(strength * (1f - player.GetStaminaPercentage()));
        }
        sustainTimer -= dt;
        if (sustainTimer <= 0f)
        {
            sustainTimer = 1f;
            var cost = 1f / (1f + power / 150f);
            if (!player.HaveEitr(cost)) { LegendsFenringPatch.Exhausted(player); return; }
            player.UseEitr(cost);
        }
        visualTimer -= dt;
        if (visualTimer <= 0f) { visualTimer = 0.2f; ApplyVisual(); }
        base.UpdateStatusEffect(dt);
    }

    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        speed *= 1.1f;
        base.ModifySpeed(baseSpeed, ref speed, character, dir);
    }

    public override void ModifyEitrRegen(ref float eitrRegen)
    {
        eitrRegen = 0f;
        base.ModifyEitrRegen(ref eitrRegen);
    }

    public override bool CanAdd(Character character) => character is Player &&
        ValheimLegends.ValheimLegends.vl_player?.vl_class == ValheimLegends.ValheimLegends.PlayerClass.Druid;

    public override bool IsDone() => ValheimLegends.ValheimLegends.vl_player?.vl_class !=
        ValheimLegends.ValheimLegends.PlayerClass.Druid;

    public override void OnDestroy()
    {
        if (visualApplied && m_character is Player player)
        {
            var equipment = player.GetComponent<VisEquipment>();
            if (equipment != null && player.GetComponent<ZNetView>()?.IsOwner() == true)
            {
                var item = player.GetInventory()?.GetAllItems().Find(candidate => candidate?.m_equipped == true);
                if (item != null)
                {
                    player.UnequipItem(item, true);
                    player.EquipItem(item, true);
                }
                else
                {
                    equipment.SetHelmetItem(0);
                    equipment.SetChestItem(0);
                    equipment.SetLegItem(0);
                    equipment.SetShoulderItem(0, 0, 1);
                }
                AccessTools.Method(typeof(Humanoid), "SetupVisEquipment", new[] { typeof(VisEquipment), typeof(bool) })
                    ?.Invoke(player, new object[] { equipment, true });
            }
        }
        base.OnDestroy();
    }

    private void ApplyVisual()
    {
        if (m_character is not Player player || player.GetComponent<ZNetView>()?.IsOwner() != true) return;
        var equipment = player.GetComponent<VisEquipment>();
        if (equipment == null) return;
        equipment.SetHelmetItem(Helmet);
        equipment.SetChestItem(Chest);
        equipment.SetLegItem(Legs);
        equipment.SetShoulderItem(Shoulder, 0, 1);
        visualApplied = true;
    }
}
