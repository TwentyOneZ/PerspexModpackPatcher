using System;
using System.Collections.Generic;
using System.Linq;
using EpicMMOSystem;
using HarmonyLib;
using UnityEngine;
using ValheimLegends;

namespace PerspexModpackPatcher;

internal static class LegendsTranslocationPatch
{
    private const string RpcRequest = "Perspex.Translocation.Request";
    private const string RpcReply = "Perspex.Translocation.Reply";
    private const string RpcCommit = "Perspex.Translocation.Commit";
    private const string RpcResult = "Perspex.Translocation.Result";
    private const string RpcCancel = "Perspex.Translocation.Cancel";
    private const string EffectName = "SE_Perspex_Translocation_CD";
    private static readonly int Mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle", "character", "character_net");
    private static readonly Dictionary<string, Request> Incoming = new();
    private static readonly Dictionary<string, float> Seen = new();
    private static Request pending;
    private static Player current;
    private static TranslocationMenu menu;

    private sealed class Request
    {
        internal string Id;
        internal long Peer;
        internal float Deadline;
        internal bool Summon;
        internal bool Accepted;
        internal Vector3 Start;
    }

    internal static void Install(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(ZNet), "Awake"), postfix: new HarmonyMethod(typeof(LegendsTranslocationPatch), nameof(Register)));
        harmony.Patch(AccessTools.Method(typeof(Player), "Update"), postfix: new HarmonyMethod(typeof(LegendsTranslocationPatch), nameof(Tick)));
        harmony.Patch(AccessTools.Method(typeof(Player), "TakeInput"), postfix: new HarmonyMethod(typeof(LegendsTranslocationPatch), nameof(BlockInput)));
        harmony.Patch(AccessTools.Method(typeof(Class_Metavoker), "Process_Input"), prefix: new HarmonyMethod(typeof(LegendsTranslocationPatch), nameof(InterceptAbility)));
        foreach (var input in new[] { "Ability1_Input_Down", "Ability1_Input_Pressed", "Ability2_Input_Down", "Ability2_Input_Pressed", "Ability3_Input_Down", "Ability3_Input_Pressed" })
        {
            var getter = AccessTools.PropertyGetter(typeof(VL_Utility), input);
            if (getter != null) harmony.Patch(getter, prefix: new HarmonyMethod(typeof(LegendsTranslocationPatch), nameof(BlockAbilities)));
        }
    }

    private static bool BlockAbilities(ref bool __result)
    {
        if (menu == null) return true;
        __result = false;
        return false;
    }

    private static bool InterceptAbility(Player player)
    {
        if (player != Player.m_localPlayer || !player.IsBlocking() || !VL_Utility.Ability2_Input_Down) return true;
        if (pending != null) Cancel("Translocation request cancelled.");
        else Open(player);
        ValheimLegends.ValheimLegends.shouldUseGuardianPower = false;
        return false;
    }

    private static void Register(ZRoutedRpc ___m_routedRpc)
    {
        ___m_routedRpc.Register<ZPackage>(RpcRequest, ReceiveRequest);
        ___m_routedRpc.Register<ZPackage>(RpcReply, ReceiveReply);
        ___m_routedRpc.Register<ZPackage>(RpcCommit, ReceiveCommit);
        ___m_routedRpc.Register<ZPackage>(RpcResult, ReceiveResult);
        ___m_routedRpc.Register<ZPackage>(RpcCancel, ReceiveCancel);
        pending = null;
        Incoming.Clear();
        Seen.Clear();
    }

    private static void Tick(Player __instance)
    {
        if (__instance != Player.m_localPlayer) return;
        if (current != __instance)
        {
            current = __instance;
            pending = null;
            Incoming.Clear();
            Seen.Clear();
            CloseMenu();
        }
        if (pending != null && (Time.realtimeSinceStartup >= pending.Deadline || !Online(pending.Peer)))
            Cancel("Translocation request expired or target disconnected.");
        foreach (var key in Incoming.Where(pair => Time.realtimeSinceStartup >= pair.Value.Deadline || !Online(pair.Value.Peer))
                     .Select(pair => pair.Key).ToArray()) Incoming.Remove(key);
        foreach (var key in Seen.Where(pair => Time.realtimeSinceStartup - pair.Value > 60f)
                     .Select(pair => pair.Key).ToArray()) Seen.Remove(key);
    }

    private static void BlockInput(ref bool __result)
    {
        if (menu != null) __result = false;
    }

    private static IEnumerable<(long id, string name)> Players()
    {
        if (ZNet.instance == null) yield break;
        foreach (var info in ZNet.instance.GetPlayerList())
        {
            var id = info.m_characterID.UserID;
            if (id != 0 && id != ZNet.GetUID() && !string.IsNullOrEmpty(info.m_name))
                yield return (id, info.m_name);
        }
    }

    private static bool Online(long id) => Players().Any(p => p.id == id);
    private static string Name(long id) => Players().FirstOrDefault(p => p.id == id).name ?? "a player";
    private static bool Valid(Player player, bool combat, out string reason)
    {
        reason = player == null || player.IsDead() ? "Player is unavailable." :
            player.IsTeleporting() ? "Already teleporting." :
            combat && (player.InAttack() || !player.CanSwitchPVP()) ? "Cannot use Translocation in combat." : null;
        return reason == null;
    }

    private static float Remaining(Player player) =>
        Mathf.Max(0, player.GetSEMan().GetStatusEffect(EffectName.GetStableHashCode())?.GetRemaningTime() ?? 0);

    private static ItemDrop.ItemData Stone(Player player) =>
        player.GetInventory().GetAllItems().FirstOrDefault(item => item.m_dropPrefab != null && item.m_dropPrefab.name == "Thunderstone");

    private static float Cost() => 25f * VL_GlobalConfigs.g_EnergyCostModifer *
        LegendsEconomyPatch.Reduction(LevelSystem.Instance.getParameter(Parameter.Agility));

    private static bool CanPay(Player player, out string reason)
    {
        reason = player.GetStamina() < Cost() ? $"Need {Cost():0.#} stamina." :
            Remaining(player) > 0 && Stone(player) == null ? "Cooldown active; one Thunderstone can bypass it." : null;
        return reason == null;
    }

    private static void Open(Player player)
    {
        if (!Valid(player, true, out var reason) || !CanPay(player, out reason)) { Message(reason); return; }
        var choices = Players().ToArray();
        if (choices.Length == 0) { Message("No online players available."); return; }
        CloseMenu();
        var host = new GameObject("Perspex Translocation Menu");
        menu = host.AddComponent<TranslocationMenu>();
        menu.Show(choices, Send);
    }

    private static void Send(long peer, bool summon)
    {
        CloseMenu();
        var player = Player.m_localPlayer;
        string reason = null;
        if (pending != null || !Online(peer) || !Valid(player, true, out reason) || !CanPay(player, out reason))
        {
            Message(reason ?? "Translocation unavailable.");
            return;
        }
        pending = new Request { Id = Guid.NewGuid().ToString("N"), Peer = peer,
            Deadline = Time.realtimeSinceStartup + 15f, Summon = summon, Start = player.transform.position };
        var package = new ZPackage();
        package.Write(pending.Id);
        package.Write(summon);
        package.Write(pending.Start);
        ZRoutedRpc.instance.InvokeRoutedRPC(peer, RpcRequest, package);
        Message($"Request sent to {Name(peer)}. Block + Ability 2 cancels.");
    }

    private static void ReceiveRequest(long sender, ZPackage package)
    {
        var id = package.ReadString();
        var summon = package.ReadBool();
        var start = package.ReadVector3();
        var player = Player.m_localPlayer;
        if (id.Length != 32 || sender == ZNet.GetUID() || !Online(sender) || !Finite(start) ||
            Seen.ContainsKey(id) || Incoming.Values.Any(r => r.Peer == sender) ||
            !Valid(player, false, out _) || !UnifiedPopup.IsAvailable()) return;
        Seen[id] = Time.realtimeSinceStartup;
        Incoming[id] = new Request { Id = id, Peer = sender, Deadline = Time.realtimeSinceStartup + 15f,
            Summon = summon, Start = start };
        var prompt = summon ? $"{Name(sender)} wants to teleport you to them. Accept?" :
            $"{Name(sender)} wants to teleport to you. Accept?";
        UnifiedPopup.Push(new YesNoPopup("Translocation", prompt,
            () => { UnifiedPopup.Pop(); Answer(id, true); },
            () => { UnifiedPopup.Pop(); Answer(id, false); }, localizeText: false, coverBackground: true));
    }

    private static void Answer(string id, bool accept)
    {
        if (!Incoming.TryGetValue(id, out var request)) return;
        var player = Player.m_localPlayer;
        var position = player.transform.position;
        var safe = Vector3.zero;
        accept &= Time.realtimeSinceStartup < request.Deadline && Online(request.Peer) &&
                  Valid(player, false, out _) &&
                  (!request.Summon || player.GetInventory().IsTeleportable(false)) &&
                  (request.Summon || SafeNear(position, player.transform, out safe));
        var package = new ZPackage();
        package.Write(id);
        package.Write(accept);
        package.Write(position);
        package.Write(safe);
        package.Write(player.transform.rotation);
        ZRoutedRpc.instance.InvokeRoutedRPC(request.Peer, RpcReply, package);
        request.Accepted = accept;
        if (!accept || !request.Summon) Incoming.Remove(id);
        Message(accept ? "Translocation accepted." : "Translocation declined or unavailable.");
    }

    private static void ReceiveReply(long sender, ZPackage package)
    {
        var id = package.ReadString();
        var accepted = package.ReadBool();
        var anchor = package.ReadVector3();
        var destination = package.ReadVector3();
        var rotation = package.ReadQuaternion();
        if (pending == null || pending.Id != id || pending.Peer != sender ||
            Time.realtimeSinceStartup >= pending.Deadline || pending.Accepted) return;
        if (!accepted) { pending = null; Message("Translocation declined."); return; }
        pending.Accepted = true;
        var player = Player.m_localPlayer;
        string reason = null;
        if (!Online(sender) || !Valid(player, true, out reason) || !CanPay(player, out reason))
        {
            Cancel(reason ?? "Target disconnected."); return;
        }
        if (!pending.Summon)
        {
            if (!player.GetInventory().IsTeleportable(false) || !SafeReply(sender, anchor, destination) ||
                !player.TeleportTo(destination, rotation, true))
            {
                Cancel("No safe destination or inventory cannot teleport."); return;
            }
            Charge(player);
            Message("Translocation successful.");
            return;
        }
        if (!SafeNear(player.transform.position, player.transform, out var safe))
        {
            Cancel("No safe destination near you."); return;
        }
        var commit = new ZPackage();
        commit.Write(id);
        commit.Write(player.transform.position);
        commit.Write(safe);
        commit.Write(player.transform.rotation);
        ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCommit, commit);
    }

    private static void ReceiveCommit(long sender, ZPackage package)
    {
        var id = package.ReadString();
        var anchor = package.ReadVector3();
        var destination = package.ReadVector3();
        var rotation = package.ReadQuaternion();
        if (!Incoming.TryGetValue(id, out var request) || request.Peer != sender ||
            !request.Summon || !request.Accepted) return;
        var player = Player.m_localPlayer;
        var success = Time.realtimeSinceStartup < request.Deadline && Online(sender) &&
                      Valid(player, false, out _) && player.GetInventory().IsTeleportable(false) &&
                      SafeReply(sender, anchor, destination) &&
                      Vector3.Distance(request.Start, anchor) <= 40f &&
                      player.TeleportTo(destination, rotation, true);
        Incoming.Remove(id);
        var result = new ZPackage();
        result.Write(id);
        result.Write(success);
        if (Online(sender)) ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcResult, result);
        Message(success ? "Summon accepted. Teleporting." : "Summon failed.");
    }

    private static void ReceiveResult(long sender, ZPackage package)
    {
        var id = package.ReadString();
        var success = package.ReadBool();
        if (pending == null || pending.Id != id || pending.Peer != sender || !pending.Summon ||
            !pending.Accepted || Time.realtimeSinceStartup >= pending.Deadline) return;
        if (success)
        {
            Charge(Player.m_localPlayer);
            Message("Summon successful.");
        }
        else Cancel("Summon failed.");
    }

    private static void ReceiveCancel(long sender, ZPackage package)
    {
        var id = package.ReadString();
        if (Incoming.TryGetValue(id, out var request) && request.Peer == sender) Incoming.Remove(id);
    }

    private static void Charge(Player player)
    {
        if (Remaining(player) > 0)
        {
            var stone = Stone(player);
            if (stone != null) player.GetInventory().RemoveOneItem(stone);
        }
        player.UseStamina(Cost());
        var previous = player.GetSEMan().GetStatusEffect(EffectName.GetStableHashCode());
        if (previous != null) player.GetSEMan().RemoveStatusEffect(previous);
        var effect = ScriptableObject.CreateInstance<PerspexTranslocationCooldown>();
        effect.m_ttl = 600f - 3f * Mathf.Clamp(LevelSystem.Instance.getParameter(Parameter.Intellect), 0, 100);
        player.GetSEMan().AddStatusEffect(effect);
        player.RaiseSkill(ValheimLegends.ValheimLegends.EvocationSkill, VL_Utility.GetWarpSkillGain);
        pending = null;
    }

    private static void Cancel(string message)
    {
        if (pending == null) return;
        var package = new ZPackage();
        package.Write(pending.Id);
        ZRoutedRpc.instance?.InvokeRoutedRPC(pending.Peer, RpcCancel, package);
        pending = null;
        Message(message);
    }

    private static bool SafeReply(long peer, Vector3 anchor, Vector3 destination)
    {
        if (!Finite(anchor) || !Finite(destination) || Vector3.Distance(anchor, destination) > 4f ||
            ZNet.instance == null) return false;
        var players = ZNet.instance.GetPlayerList();
        if (!players.Any(p => p.m_characterID.UserID == peer)) return false;
        var info = players.First(p => p.m_characterID.UserID == peer);
        return !info.m_publicPosition || Vector3.Distance(info.m_position, anchor) <= 20f;
    }

    private static bool Finite(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsNaN(point.y) && !float.IsNaN(point.z) &&
        !float.IsInfinity(point.x) && !float.IsInfinity(point.y) && !float.IsInfinity(point.z);

    private static bool SafeNear(Vector3 anchor, Transform body, out Vector3 destination)
    {
        destination = Vector3.zero;
        if (!Finite(anchor) || ZoneSystem.instance == null || ZNetScene.instance == null ||
            !ZNetScene.instance.IsAreaReady(anchor)) return false;
        foreach (var offset in new[] { body.right, -body.right, body.forward, -body.forward })
        {
            var sample = anchor + offset * 2.5f;
            if (!ZoneSystem.instance.FindFloor(sample + Vector3.up * 2f, out var floor) ||
                floor <= ZoneSystem.instance.m_waterLevel + 0.2f || Mathf.Abs(floor - anchor.y) > 2f) continue;
            var spot = new Vector3(sample.x, floor + 0.1f, sample.z);
            if (Physics.CheckCapsule(spot + Vector3.up * 0.45f, spot + Vector3.up * 1.65f, 0.38f, Mask)) continue;
            destination = spot;
            return true;
        }
        return false;
    }

    private static void CloseMenu()
    {
        if (menu != null) UnityEngine.Object.Destroy(menu.gameObject);
        menu = null;
    }

    private static void Message(string value) => Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, value);

    private sealed class PerspexTranslocationCooldown : StatusEffect
    {
        public PerspexTranslocationCooldown()
        {
            name = EffectName;
            m_name = "Translocation Cooldown";
            m_tooltip = m_name;
            m_icon = ValheimLegends.ValheimLegends.Ability2_Sprite;
        }

        public override bool CanAdd(Character character) => character.IsPlayer();
    }

    private sealed class TranslocationMenu : MonoBehaviour
    {
        private (long id, string name)[] players;
        private Action<long, bool> select;
        private Vector2 scroll;
        private bool priorVisible;
        private CursorLockMode priorLock;

        private void Awake()
        {
            priorVisible = Cursor.visible;
            priorLock = Cursor.lockState;
        }

        internal void Show((long id, string name)[] choices, Action<long, bool> action)
        {
            players = choices;
            select = action;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Player.m_localPlayer == null || ZNet.instance == null) CloseMenu();
            else { Cursor.visible = true; Cursor.lockState = CursorLockMode.None; }
        }

        private void OnDestroy()
        {
            Cursor.visible = priorVisible;
            Cursor.lockState = priorLock;
        }

        private void OnGUI()
        {
            if (players == null) return;
            var width = Mathf.Min(620, Screen.width - 40);
            var height = Mathf.Min(540, Screen.height - 40);
            var rect = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
            GUI.Box(new Rect(0, 0, Screen.width, Screen.height), "");
            GUI.Box(rect, "TRANSLOCATION");
            GUI.Label(new Rect(rect.x + 20, rect.y + 30, width - 40, 30),
                "Choose a player. They must accept the request.");
            var view = new Rect(rect.x + 20, rect.y + 65, width - 40, height - 125);
            scroll = GUI.BeginScrollView(view, scroll, new Rect(0, 0, width - 65, players.Length * 46));
            for (var index = 0; index < players.Length; index++)
            {
                var row = players[index];
                GUI.Label(new Rect(10, index * 46, width - 290, 40), row.name);
                if (GUI.Button(new Rect(width - 275, index * 46, 100, 36), "Go To")) select(row.id, false);
                if (GUI.Button(new Rect(width - 165, index * 46, 100, 36), "Summon")) select(row.id, true);
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(rect.x + width / 2f - 60, rect.yMax - 52, 120, 36), "Cancel")) CloseMenu();
        }
    }
}
