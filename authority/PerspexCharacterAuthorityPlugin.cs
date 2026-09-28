using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PerspexCharacterAuthority;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("twentyonez.perspex.patcher")]
public sealed class PerspexCharacterAuthorityPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "TwentyOneZ.PerspexCharacterAuthority";
    public const string PluginName = "PerspexCharacterAuthority";
    public const string PluginVersion = "1.0.6";
    public const int ProtocolVersion = 1;
    private const string RpcName = "PCA_Message";
    private const string VLExtension = "ValheimLegends";
    private const string WorldProfileExtension = "PCA.WorldProfile";
    private const string FreshProgressionExtension = "PCA.FreshProgression";
    private const string FreshAppliedExtension = "PCA.FreshApplied";

    private enum MessageKind { Hello = 1, Identify = 2, Snapshot = 3, Submit = 4, Denied = 5, SaveAck = 6 }
    private sealed class Session { public string AccountId; public long CharacterId; public string CharacterName; }

    internal static PerspexCharacterAuthorityPlugin Instance;
    public static bool IsAuthorityActiveForCurrentSession => Instance != null && Instance.Enabled && Instance.handshake?.State != PcaHandshakeState.Vanilla;
    private readonly Dictionary<ZRpc, Session> sessions = new Dictionary<ZRpc, Session>();
    private readonly Dictionary<string, ZRpc> activeCharacters = new Dictionary<string, ZRpc>(StringComparer.Ordinal);
    private readonly Dictionary<ZRpc, string> authenticatedAccounts = new Dictionary<ZRpc, string>();
    private readonly HashSet<ZRpc> registeredRpcs = new HashSet<ZRpc>();
    private readonly Dictionary<ZRpc, PcaServerConnectionState> connections = new Dictionary<ZRpc, PcaServerConnectionState>();
    private readonly PcaPendingActionGate freshProgression = new PcaPendingActionGate();
    private readonly PcaPendingActionGate vlImport = new PcaPendingActionGate();
    private readonly PcaSubmitTracker submits = new PcaSubmitTracker();
    private PcaStore store;
    private PcaHandshakeStateMachine handshake;
    private ConfigEntry<bool> pcaEnabled;
    private ConfigEntry<bool> requireServerAuthority;
    private ConfigEntry<int> handshakeTimeoutSeconds;
    private ConfigEntry<bool> singleCharacter;
    private ConfigEntry<string> firstJoinMode;
    private ConfigEntry<int> autosaveSeconds;
    private ConfigEntry<bool> saveOnLogout;
    private ConfigEntry<bool> saveOnDisconnect;
    private ConfigEntry<bool> saveOnShutdown;
    private ConfigEntry<bool> backupsEnabled;
    private ConfigEntry<int> maximumBackups;
    private ConfigEntry<int> maxSnapshotSizeMb;
    private ConfigEntry<bool> debugLogging;
    private ConfigEntry<string> diagnosticLevel;
    private ConfigEntry<bool> writeDiagnosticFile;
    private ConfigEntry<bool> mirrorToGameLog;
    private ConfigEntry<float> pendingStateInterval;
    private PcaDiagnostics diagnostics;
    private ZRpc clientRpc;
    private CharacterSnapshot clientSnapshot;
    private bool freshProgressionApplied;
    private bool serverDetected;
    private long lastServerSnapshotTicks;
    private int nextConnectionId;
    private string role = "UNKNOWN";
    private float clientHandshakeStartedAt;
    private float clientLastStateLogAt;
    private string pendingClientRejection;

    private int MaxBytes => Math.Max(1, maxSnapshotSizeMb.Value) * 1024 * 1024;
    private int HandshakeTimeoutSeconds => Math.Max(5, Math.Min(60, handshakeTimeoutSeconds.Value));
    internal bool Enabled => pcaEnabled != null && pcaEnabled.Value;

    private void Awake()
    {
        Instance = this;
        pcaEnabled = Config.Bind("General", "Enabled", true, "Enable server-authoritative character snapshots.");
        debugLogging = Config.Bind("General", "DebugLogging", false, "Legacy switch; enables at least Verbose diagnostics.");
        diagnosticLevel = Config.Bind("Diagnostics", "Level", "Basic", "Off, Basic, Verbose, or Trace.");
        writeDiagnosticFile = Config.Bind("Diagnostics", "WriteDiagnosticFile", true, "Write a dedicated PCA diagnostic log per process.");
        mirrorToGameLog = Config.Bind("Diagnostics", "MirrorToGameLog", true, "Mirror diagnostic events through ZLog.");
        pendingStateInterval = Config.Bind("Diagnostics", "PendingStateIntervalSeconds", 1f, "Pending state log interval, clamped to 0.25-60 seconds.");
        requireServerAuthority = Config.Bind("Authority", "RequireServerAuthority", true, "Fail closed when a multiplayer server does not complete the PCA handshake.");
        handshakeTimeoutSeconds = Config.Bind("Authority", "HandshakeTimeoutSeconds", 15, "Seconds to wait for the PCA handshake (clamped to 5-60).");
        singleCharacter = Config.Bind("Access", "SingleCharacterPerAccount", true, "Bind each account to one persistent CharacterId.");
        firstJoinMode = Config.Bind("FirstJoin", "Mode", "PreserveCharacter", "PreserveCharacter or FreshProgression.");
        autosaveSeconds = Config.Bind("Saving", "AutosaveIntervalSeconds", 300, "Client snapshot submit interval.");
        saveOnLogout = Config.Bind("Saving", "SaveOnLogout", true, "Submit the local snapshot during teardown.");
        saveOnDisconnect = Config.Bind("Saving", "SaveOnDisconnect", true, "Submit the local snapshot when the network object is destroyed.");
        saveOnShutdown = Config.Bind("Saving", "SaveOnServerShutdown", true, "Submit the local snapshot during application shutdown.");
        backupsEnabled = Config.Bind("Backups", "Enabled", true, "Keep rotating snapshots before replacement.");
        maximumBackups = Config.Bind("Backups", "MaximumBackupsPerCharacter", 10, "Maximum backups retained per character.");
        maxSnapshotSizeMb = Config.Bind("Limits", "MaxSnapshotSizeMB", 16, "Maximum accepted snapshot payload.");
        handshake = new PcaHandshakeStateMachine(ProtocolVersion, requireServerAuthority.Value);
        var storePath = Path.Combine(Paths.ConfigPath, "PerspexCharacterAuthority");
        store = new PcaStore(storePath, MaxBytes, backupsEnabled.Value, maximumBackups.Value);
        diagnostics = new PcaDiagnostics(Logger, diagnosticLevel.Value, debugLogging.Value, writeDiagnosticFile.Value, mirrorToGameLog.Value,
            Path.Combine(storePath, "diagnostics"), pendingStateInterval.Value);
        Diag(PcaDiagnosticLevel.Basic, null, "BOOT", "version=" + PluginVersion + " protocol=" + ProtocolVersion + " role=UNKNOWN enabled=" + Enabled +
            " authorityRequired=" + requireServerAuthority.Value + " timeout=" + HandshakeTimeoutSeconds + " diagnostics=" + diagnostics.Level +
            " gameVersion=" + global::Version.CurrentVersion + " bepinexVersion=" + typeof(BaseUnityPlugin).Assembly.GetName().Version);
        Diag(PcaDiagnosticLevel.Basic, null, "CONFIG", "singleCharacter=" + singleCharacter.Value + " firstJoin=" + firstJoinMode.Value + " maxSnapshotMB=" + maxSnapshotSizeMb.Value);
        Diag(PcaDiagnosticLevel.Basic, null, "AUTHORITY_BOUNDARY", "CHARACTER_AUTHORITY_BOUNDARY=SPAWN NETWORK_ADDPEER_GATING=false");
        Diag(PcaDiagnosticLevel.Basic, null, "STORE_PATH", storePath);
        new Harmony(PluginGuid).PatchAll(typeof(PerspexCharacterAuthorityPlugin).Assembly);
        Diag(PcaDiagnosticLevel.Basic, null, "HARMONY_PATCHES_INSTALLED", "assembly=" + typeof(PerspexCharacterAuthorityPlugin).Assembly.GetName().Version);
        new Terminal.ConsoleCommand("pca", "PCA server character administration", RunCommand, onlyServer: true, onlyAdmin: true);
        InvokeRepeating(nameof(Autosave), Math.Max(10, autosaveSeconds.Value), Math.Max(10, autosaveSeconds.Value));
    }

    private void OnApplicationQuit()
    {
        if (saveOnShutdown.Value) SubmitLocalSnapshot("shutdown");
    }

    private void OnDestroy()
    {
        diagnostics?.Dispose();
        if (Instance == this) Instance = null;
    }

    private void Autosave() => SubmitLocalSnapshot("autosave");

    private void Update()
    {
        var network = ZNet.instance;
        if (network == null) return;
        EnsureRole(network);
        if (!network.IsServer())
        {
            LogPendingClientState();
            return;
        }
        UpdateServerConnections(network);
    }

    private void UpdateServerConnections(ZNet network)
    {
        if (connections.Count == 0) return;
        var now = Time.realtimeSinceStartup;
        var expired = new List<ZRpc>();
        foreach (var pair in new List<KeyValuePair<ZRpc, PcaServerConnectionState>>(connections))
        {
            var state = pair.Value;
            if (state.PeerInfoSeen && !state.HelloSent) TryAdvanceNativeAuthentication(network, pair.Key, state);
            if (diagnostics.Level >= PcaDiagnosticLevel.Trace && !state.SessionAuthorized && now - state.LastStateLogAt >= diagnostics.PendingInterval)
            {
                state.LastStateLogAt = now;
                Diag(PcaDiagnosticLevel.Trace, pair.Key, "STATE", ConnectionDetails(network, pair.Key, state, now));
            }
            if (state.PeerInfoSeen && !state.SessionAuthorized && now - state.PeerInfoAt >= HandshakeTimeoutSeconds) expired.Add(pair.Key);
        }
        foreach (var rpc in expired)
        {
            if (!connections.TryGetValue(rpc, out var state)) continue;
            Diag(PcaDiagnosticLevel.Basic, rpc, "SERVER_TIMEOUT", "phase=" + state.TimeoutPhase + " elapsed=" + (now - state.PeerInfoAt).ToString("F1") + "s last=" + state.LastEvent);
            Deny(rpc, "PCA handshake timed out during " + state.TimeoutPhase + ".");
            rpc.Invoke("Error", 8);
            DisconnectPeer(rpc);
        }
    }

    internal void OnNewConnection(ZNet network, ZNetPeer peer)
    {
        var rpc = peer?.m_rpc;
        if (!Enabled || rpc == null) return;
        EnsureRole(network);
        var state = GetOrCreateConnection(rpc);
        Diag(PcaDiagnosticLevel.Trace, rpc, "ON_NEW_CONNECTION_ENTER", PeerDetails(peer, state));
        if (!registeredRpcs.Add(rpc))
        {
            Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_ALREADY_REGISTERED", PeerDetails(peer, state));
            return;
        }
        Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_REGISTER_ATTEMPT", "rpcHash=" + rpc.GetHashCode().ToString("X8"));
        rpc.Register<ZPackage>(RpcName, HandleMessage);
        state.RpcRegistered = true;
        Diag(PcaDiagnosticLevel.Basic, rpc, "RPC_REGISTER_OK", PeerDetails(peer, state));
        Audit("RPC registered for new " + (network.IsServer() ? "server" : "client") + " peer.");
        if (network.IsServer()) return;
        clientRpc = rpc;
        clientHandshakeStartedAt = Time.realtimeSinceStartup;
        clientLastStateLogAt = clientHandshakeStartedAt;
        BeginWaitingForHello();
    }

    internal void OnNewConnectionExit(ZNet network, ZNetPeer peer)
    {
        if (!Enabled || peer?.m_rpc == null) return;
        Diag(PcaDiagnosticLevel.Trace, peer.m_rpc, "ON_NEW_CONNECTION_EXIT", PeerDetails(peer, GetOrCreateConnection(peer.m_rpc)));
    }

    internal void OnPeerInfoEnter(ZNet network, ZRpc rpc)
    {
        if (!Enabled || rpc == null) return;
        EnsureRole(network);
        var state = GetOrCreateConnection(rpc);
        state.MarkPeerInfo(Time.realtimeSinceStartup);
        Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_PEERINFO_ENTER", ConnectionDetails(network, rpc, state, Time.realtimeSinceStartup));
    }

    internal void NativeHandshakeEvent(ZNet network, ZRpc rpc, string eventName)
    {
        if (!Enabled || rpc == null) return;
        EnsureRole(network);
        var state = GetOrCreateConnection(rpc);
        ZNetPeer peer = null;
        foreach (var candidate in network.GetPeers()) if (candidate.m_rpc == rpc) { peer = candidate; break; }
        Diag(PcaDiagnosticLevel.Trace, rpc, eventName, PeerDetails(peer, state));
    }

    internal void OnPeerInfo(ZNet network, ZRpc rpc)
    {
        if (!Enabled || rpc == null) return;
        EnsureRole(network);
        var state = GetOrCreateConnection(rpc);
        if (!state.PeerInfoSeen) state.MarkPeerInfo(Time.realtimeSinceStartup);
        Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_PEERINFO_EXIT", ConnectionDetails(network, rpc, state, Time.realtimeSinceStartup));
        if (network.IsServer())
        {
            TryAdvanceNativeAuthentication(network, rpc, state, logWait: true);
            return;
        }
        WaitForServerHandshake(rpc);
    }

    private void TryAdvanceNativeAuthentication(ZNet network, ZRpc rpc, PcaServerConnectionState state, bool logWait = false)
    {
        if (state.HelloSent) return;
        if (!TryGetAuthenticatedAccount(network, rpc, out var accountId, out var peer, out var reason))
        {
            if (logWait) Diag(PcaDiagnosticLevel.Trace, rpc, "AUTH_WAIT", "reason=" + reason + " " + PeerDetails(peer, state));
            return;
        }
        state.MarkAuthenticated();
        authenticatedAccounts[rpc] = accountId;
        Diag(PcaDiagnosticLevel.Basic, rpc, "NATIVE_AUTHENTICATED", "account=" + MaskAccount(accountId) + " " + PeerDetails(peer, state));
        if (!state.TryMarkHelloSent()) return;
        Send(rpc, MessageKind.Hello, Array.Empty<byte>());
        Audit("Peer authenticated as " + MaskAccount(accountId) + "; Hello sent.");
    }

    internal void ObserveNativeAddPeer(ZNetPeer peer, bool zdo)
    {
        if (!Enabled || ZNet.instance == null || !ZNet.instance.IsServer() || peer?.m_rpc == null) return;
        var state = GetOrCreateConnection(peer.m_rpc);
        if (zdo) state.MarkZdoPeerAdded(); else state.MarkRoutedPeerAdded();
        Diag(PcaDiagnosticLevel.Trace, peer.m_rpc, zdo ? "ZDO_ADD_PEER" : "ROUTED_ADD_PEER",
            "CALL ALLOW networkGate=false characterAuthorized=" + state.SessionAuthorized + " " + PeerDetails(peer, state));
        if (zdo) TryAdvanceNativeAuthentication(ZNet.instance, peer.m_rpc, state, logWait: true);
    }

    private void WaitForServerHandshake(ZRpc rpc)
    {
        if (ZNet.instance == null || ZNet.instance.IsServer()) return;
        clientRpc = rpc;
        if (!BeginWaitingForHello())
        {
            handshake.ReceiveDenied();
            Warn("Selected character is unavailable before PCA handshake.");
            DisconnectRejectedClient("Selected character is unavailable before PCA handshake.");
            return;
        }
        CancelInvoke(nameof(HandshakeTimedOut));
        Invoke(nameof(HandshakeTimedOut), HandshakeTimeoutSeconds);
        Diag(PcaDiagnosticLevel.Basic, rpc, "CLIENT_WAIT_HELLO", "timeout=" + HandshakeTimeoutSeconds + " characterId=" + handshake.CharacterId);
    }

    private bool BeginWaitingForHello()
    {
        if (handshake.State != PcaHandshakeState.Vanilla) return true;
        var profile = Game.instance?.GetPlayerProfile();
        if (profile == null) return false;
        serverDetected = false;
        handshake.Begin(profile.GetPlayerID());
        Audit("Waiting for Hello for character=" + profile.GetPlayerID() + ".");
        return true;
    }

    private void SendClientIdentify()
    {
        try
        {
            var snapshot = BuildLocalSnapshot();
            Send(clientRpc, MessageKind.Identify, PcaSnapshotCodec.Serialize(snapshot, MaxBytes));
            Audit("Identify sent for character=" + snapshot.CharacterId + ".");
        }
        catch (Exception ex)
        {
            Warn("Could not identify local character: " + ex.Message);
            handshake.ReceiveDenied();
            DisconnectRejectedClient("Character authority identification failed.");
        }
    }

    private void HandshakeTimedOut()
    {
        var action = handshake.Timeout();
        Diag(PcaDiagnosticLevel.Basic, clientRpc, "CLIENT_TIMEOUT", "elapsed=" + (Time.realtimeSinceStartup - clientHandshakeStartedAt).ToString("F1") + "s action=" + action + " serverDetected=" + serverDetected);
        if (action == PcaHandshakeAction.VanillaFallback)
        {
            Warn("PCA handshake timed out; server authority is explicitly optional, using vanilla persistence.");
            Game.instance?.RequestRespawn(0f);
        }
        else if (action == PcaHandshakeAction.Deny)
        {
            const string message = "Character authority handshake failed. This server requires server-authoritative characters.";
            Warn(message);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, message);
            DisconnectRejectedClient(message);
        }
    }

    private void HandleMessage(ZRpc rpc, ZPackage package)
    {
        if (package == null || package.Size() > MaxBytes + 65536) { RejectProtocolMessage(rpc, "PCA payload is too large."); return; }
        try
        {
            var protocol = package.ReadInt();
            var kind = (MessageKind)package.ReadInt();
            var payload = package.ReadByteArray();
            Diag(PcaDiagnosticLevel.Verbose, rpc, "RECV_" + kind, "protocol=" + protocol + " bytes=" + (payload?.Length ?? 0) + " handshake=" + handshake.State);
            if (protocol != ProtocolVersion)
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) Deny(rpc, "PCA protocol version mismatch.");
                else { handshake.ReceiveDenied(); Warn("PCA protocol version mismatch."); DisconnectRejectedClient("PCA protocol version mismatch."); }
                return;
            }
            if (payload == null || payload.Length > MaxBytes + 65536) { RejectProtocolMessage(rpc, "PCA payload is too large."); return; }
            if (ZNet.instance != null && ZNet.instance.IsServer()) HandleServerMessage(rpc, kind, payload);
            else HandleClientMessage(kind, payload);
        }
        catch (Exception ex)
        {
            Warn("Rejected malformed PCA message: " + ex.Message);
            RejectProtocolMessage(rpc, "Invalid PCA message.");
        }
    }

    private void RejectProtocolMessage(ZRpc rpc, string message)
    {
        if (ZNet.instance != null && ZNet.instance.IsServer()) Deny(rpc, message);
        else { handshake.ReceiveDenied(); Warn(message); DisconnectRejectedClient(message); }
    }

    private void HandleServerMessage(ZRpc rpc, MessageKind kind, byte[] payload)
    {
        if (!authenticatedAccounts.TryGetValue(rpc, out var accountId))
        {
            Deny(rpc, "Peer has not completed authenticated network setup.");
            return;
        }
        if (kind == MessageKind.Identify)
        {
            if (sessions.ContainsKey(rpc)) { Deny(rpc, "Character session is already authorized."); return; }
            if (connections.TryGetValue(rpc, out var state)) state.MarkIdentify();
            IdentifyServerCharacter(rpc, accountId, payload);
            return;
        }
        if (kind == MessageKind.Submit) { SaveSubmittedCharacter(rpc, payload); return; }
        Deny(rpc, "Unexpected PCA message.");
    }

    private void IdentifyServerCharacter(ZRpc rpc, string accountId, byte[] payload)
    {
        if (!PcaSnapshotCodec.TryDeserialize(payload, MaxBytes, out var incoming, out var error)) { Deny(rpc, "Invalid character snapshot: " + error); return; }
        Audit("Identify received from " + MaskAccount(accountId) + " for character=" + incoming.CharacterId + ".");
        if (!store.TryAuthorize(accountId, incoming.CharacterId, incoming.CharacterName, singleCharacter.Value, out var denial)) { Deny(rpc, denial); return; }

        CharacterSnapshot authoritative;
        Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_LOOKUP", SnapshotDetails(incoming));
        if (store.TryRead(accountId, incoming.CharacterId, out authoritative, out var recovered))
        {
            Diag(PcaDiagnosticLevel.Verbose, rpc, recovered ? "SNAPSHOT_BACKUP_RECOVERED" : "SNAPSHOT_FOUND", SnapshotDetails(authoritative));
            if (authoritative.CharacterId != incoming.CharacterId || !string.Equals(authoritative.AccountId, accountId, StringComparison.Ordinal))
            {
                Deny(rpc, "Stored character identity validation failed.");
                return;
            }
            if (recovered)
            {
                Warn("Snapshot checksum mismatch; restoring the newest valid backup for account=" + MaskAccount(accountId) + ".");
                if (!store.TrySave(authoritative, out error)) { Deny(rpc, "Could not restore the valid backup: " + error); return; }
            }
            Audit("Server snapshot loaded for account=" + MaskAccount(accountId) + " character=" + incoming.CharacterId + ".");
        }
        else
        {
            Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_NOT_FOUND", "account=" + MaskAccount(accountId) + " characterId=" + incoming.CharacterId);
            if (store.HasSnapshotData(accountId, incoming.CharacterId))
            {
                Deny(rpc, "No valid server snapshot or backup is available; refusing local overwrite.");
                return;
            }
            authoritative = incoming;
            authoritative.AccountId = accountId;
            authoritative.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            authoritative.UpdatedUtcTicks = authoritative.CreatedUtcTicks;
            if (string.Equals(firstJoinMode.Value, "FreshProgression", StringComparison.OrdinalIgnoreCase))
            {
                authoritative.Extensions[VLExtension] = VLBridge.EmptyState();
                authoritative.Extensions[FreshProgressionExtension] = new ExtensionPayload { SchemaVersion = 1, Data = Array.Empty<byte>() };
            }
            Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_WRITE_BEGIN", SnapshotDetails(authoritative));
            if (!store.TrySave(authoritative, out error)) { Diag(PcaDiagnosticLevel.Basic, rpc, "SNAPSHOT_WRITE_FAIL", error); Deny(rpc, "Could not create server snapshot: " + error); return; }
            Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_CREATED", SnapshotDetails(authoritative));
            Audit("Server snapshot created for account=" + MaskAccount(accountId) + " character=" + incoming.CharacterId + ".");
        }
        var leaseKey = accountId + "\n" + authoritative.CharacterId;
        if (activeCharacters.TryGetValue(leaseKey, out var activeRpc) && activeRpc != rpc && activeRpc.IsConnected())
        {
            Deny(rpc, "This character already has an active session.");
            return;
        }
        if (activeCharacters.TryGetValue(leaseKey, out activeRpc) && activeRpc != rpc) sessions.Remove(activeRpc);
        activeCharacters[leaseKey] = rpc;
        sessions[rpc] = new Session { AccountId = accountId, CharacterId = authoritative.CharacterId, CharacterName = authoritative.CharacterName };
        if (connections.TryGetValue(rpc, out var connection)) connection.MarkAuthorized();
        Send(rpc, MessageKind.Snapshot, PcaSnapshotCodec.Serialize(authoritative, MaxBytes));
        connection?.MarkSnapshotSent();
        Audit("Binding accepted for account=" + MaskAccount(accountId) + " character=" + authoritative.CharacterId + ".");
        Audit("Authoritative snapshot sent for character=" + authoritative.CharacterId + ".");
    }

    private void SaveSubmittedCharacter(ZRpc rpc, byte[] payload)
    {
        if (!sessions.TryGetValue(rpc, out var session)) { Deny(rpc, "Character has not been authorized."); return; }
        if (!PcaSnapshotCodec.TryDeserialize(payload, MaxBytes, out var submitted, out var error)) { Deny(rpc, "Invalid submitted snapshot: " + error); return; }
        if (submitted.CharacterId != session.CharacterId) { Deny(rpc, "Submitted character does not match the authenticated session."); return; }
        submitted.AccountId = session.AccountId;
        submitted.CharacterName = string.IsNullOrWhiteSpace(submitted.CharacterName) ? session.CharacterName : submitted.CharacterName;
        if (store.TryRead(session.AccountId, session.CharacterId, out var current, out _))
        {
            submitted.CreatedUtcTicks = current.CreatedUtcTicks;
            var freshApplied = submitted.Extensions.Remove(FreshAppliedExtension);
            if (!freshApplied && current.Extensions.ContainsKey(FreshProgressionExtension) && current.Extensions.TryGetValue(VLExtension, out var freshVl))
                submitted.Extensions[VLExtension] = freshVl;
            foreach (var extension in current.Extensions)
                if ((extension.Key != FreshProgressionExtension || !freshApplied) && !submitted.Extensions.ContainsKey(extension.Key))
                    submitted.Extensions.Add(extension.Key, extension.Value);
        }
        submitted.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_WRITE_BEGIN", SnapshotDetails(submitted));
        if (!store.TrySave(submitted, out error)) { Diag(PcaDiagnosticLevel.Basic, rpc, "SNAPSHOT_WRITE_FAIL", error); Warn("Snapshot save failed: " + error); return; }
        Diag(PcaDiagnosticLevel.Verbose, rpc, "SNAPSHOT_WRITE_OK", SnapshotDetails(submitted));
        session.CharacterName = submitted.CharacterName;
        Send(rpc, MessageKind.SaveAck, Array.Empty<byte>());
        Audit("Snapshot committed atomically for account=" + MaskAccount(session.AccountId) + " character=" + session.CharacterId + ".");
    }

    private void HandleClientMessage(MessageKind kind, byte[] payload)
    {
        if (kind == MessageKind.Hello)
        {
            if (!BeginWaitingForHello()) { handshake.ReceiveDenied(); DisconnectRejectedClient("Selected character is unavailable."); return; }
            var previous = handshake.State;
            var helloAction = handshake.ReceiveHello(ProtocolVersion);
            Diag(PcaDiagnosticLevel.Verbose, clientRpc, "HANDSHAKE_TRANSITION", "message=Hello state=" + previous + " next=" + handshake.State + " action=" + helloAction);
            if (helloAction != PcaHandshakeAction.SendIdentify) return;
            serverDetected = true;
            Audit("Hello received; sending Identify.");
            SendClientIdentify();
            return;
        }
        if (kind == MessageKind.Denied)
        {
            var previous = handshake.State;
            handshake.ReceiveDenied();
            Diag(PcaDiagnosticLevel.Verbose, clientRpc, "HANDSHAKE_TRANSITION", "message=Denied state=" + previous + " next=" + handshake.State);
            CancelInvoke(nameof(HandshakeTimedOut));
            var message = payload == null ? "Character access denied." : System.Text.Encoding.UTF8.GetString(payload);
            Warn(message);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, message);
            DisconnectRejectedClient(message);
            return;
        }
        if (kind == MessageKind.SaveAck)
        {
            if (!handshake.CanSubmit || !submits.Acknowledge()) { RejectProtocolMessage(clientRpc, "Unexpected PCA SaveAck."); return; }
            freshProgressionApplied = false;
            return;
        }
        if (kind != MessageKind.Snapshot)
        {
            handshake.ReceiveDenied();
            Warn("Unexpected PCA server message.");
            DisconnectRejectedClient("Unexpected character authority message.");
            return;
        }
        if (!PcaSnapshotCodec.TryDeserialize(payload, MaxBytes, out var snapshot, out var error))
        {
            handshake.ReceiveDenied();
            Warn("Invalid server snapshot: " + error);
            DisconnectRejectedClient("Invalid server character snapshot.");
            return;
        }
        serverDetected = true;
        var snapshotPrevious = handshake.State;
        if (snapshotPrevious != PcaHandshakeState.WaitingForSnapshot || snapshot.CharacterId != handshake.CharacterId)
        {
            handshake.ReceiveDenied();
            Warn("Server snapshot identity does not match the selected character.");
            DisconnectRejectedClient("Server snapshot identity does not match the selected character.");
            return;
        }
        var profile = Game.instance?.GetPlayerProfile();
        if (profile == null) { handshake.ReceiveDenied(); DisconnectRejectedClient("Selected character is unavailable."); return; }
        AccessTools.Field(typeof(PlayerProfile), "m_playerData").SetValue(profile, snapshot.VanillaPlayerData);
        if (snapshot.Extensions.TryGetValue(WorldProfileExtension, out var worldProfile)) WorldProfileBridge.Import(profile, worldProfile, MaxBytes);
        clientSnapshot = snapshot;
        vlImport.Load(true);
        freshProgression.Load(snapshot.Extensions.ContainsKey(FreshProgressionExtension));
        lastServerSnapshotTicks = snapshot.UpdatedUtcTicks;
        var snapshotAction = handshake.ReceiveSnapshot(ProtocolVersion, snapshot.CharacterId);
        Diag(PcaDiagnosticLevel.Verbose, clientRpc, "HANDSHAKE_TRANSITION", "message=Snapshot state=" + snapshotPrevious + " next=" + handshake.State + " action=" + snapshotAction);
        CancelInvoke(nameof(HandshakeTimedOut));
        Diag(PcaDiagnosticLevel.Basic, clientRpc, "SNAPSHOT_APPLIED", SnapshotDetails(snapshot));
        Audit("Authoritative snapshot applied before spawn for character=" + snapshot.CharacterId + ".");
        Game.instance.RequestRespawn(0f);
    }

    internal bool BlockRespawn()
    {
        var blocked = Enabled && !handshake.CanSpawn;
        Diag(PcaDiagnosticLevel.Trace, clientRpc, "RESPAWN_REQUEST", (blocked ? "BLOCK" : "ALLOW") + " handshake=" + handshake.State);
        return blocked;
    }

    internal bool BlockPlayerSpawn()
    {
        var blocked = Enabled && !handshake.CanSpawn;
        Diag(PcaDiagnosticLevel.Trace, clientRpc, "PLAYER_SPAWN", (blocked ? "BLOCK" : "ALLOW") + " handshake=" + handshake.State);
        return blocked;
    }

    internal bool AuthorizeNativePlayerId(ZRpc rpc, long playerId)
    {
        Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_PLAYER_ID", "playerId=" + playerId + " session=" + sessions.ContainsKey(rpc));
        // Native PlayerID only records peer identity; the actual character boundary is Game.SpawnPlayer/CharacterID.
        return true;
    }

    internal bool AuthorizeNativeCharacterId(ZRpc rpc, ZDOID characterId)
    {
        Diag(PcaDiagnosticLevel.Trace, rpc, "RPC_CHARACTER_ID", "characterId=" + characterId + " session=" + sessions.ContainsKey(rpc));
        if (characterId == ZDOID.None) return true;
        if (sessions.ContainsKey(rpc))
        {
            Audit("Character ZDOID accepted after PCA authorization.");
            return true;
        }
        Deny(rpc, "Character was not authorized before spawn.");
        rpc.Invoke("Error", 8);
        return false;
    }

    internal void ResetClientSession(ZNet network)
    {
        sessions.Clear();
        activeCharacters.Clear();
        authenticatedAccounts.Clear();
        registeredRpcs.Clear();
        connections.Clear();
        if (network != null && network.IsServer()) return;
        CancelInvoke(nameof(HandshakeTimedOut));
        handshake.Reset();
        clientRpc = null;
        clientSnapshot = null;
        vlImport.Load(false);
        freshProgression.Load(false);
        freshProgressionApplied = false;
        submits.Reset();
        serverDetected = false;
    }

    internal void ApplyPendingVLExtension(Player player)
    {
        Diag(PcaDiagnosticLevel.Trace, clientRpc, "PLAYER_ON_SPAWNED", "handshake=" + handshake.State + " snapshot=" + (clientSnapshot != null));
        if (!handshake.CanSubmit || clientSnapshot == null) { Diag(PcaDiagnosticLevel.Trace, clientRpc, "VL_IMPORT", "SKIP reason=no_authoritative_snapshot"); return; }
        if (!vlImport.TryApply()) { Diag(PcaDiagnosticLevel.Trace, clientRpc, "VL_IMPORT", "SKIP reason=already_applied"); return; }
        if (clientSnapshot.Extensions.TryGetValue(VLExtension, out var extension)) VLBridge.Import(player, extension);
    }

    internal void ApplyFreshProgression(PlayerProfile profile, Player player)
    {
        if (player == null || profile == null || !freshProgression.TryApply()) return;
        Diag(PcaDiagnosticLevel.Verbose, clientRpc, "FRESH_PROGRESSION_APPLY", "characterId=" + handshake.CharacterId);
        freshProgressionApplied = true;
        player.UnequipAllItems();
        player.GetInventory().RemoveAll();
        player.ResetCharacter();
        player.ClearFood();
        player.SetGuardianPower(string.Empty);
        player.m_guardianPowerCooldown = 0f;
        player.GiveDefaultItems();
        VLBridge.Reset(player);
        player.m_customData["HS_HearthPos"] = "0|0|0";
        player.m_customData["Perspex_LastDeathPos"] = "0|0|0";
        profile.SavePlayerData(player);
        Audit("FreshProgression applied before OnSpawned.");
        SubmitLocalSnapshot("fresh progression");
    }

    internal void SubmitLocalSnapshot(string reason)
    {
        if (!Enabled || !handshake.CanSubmit || clientRpc == null || !clientRpc.IsConnected())
        {
            Diag(PcaDiagnosticLevel.Trace, clientRpc, "SUBMIT_SKIP", "reason=" + reason + " enabled=" + Enabled + " handshake=" + handshake.State +
                " rpc=" + (clientRpc != null) + " connected=" + (clientRpc?.IsConnected() ?? false));
            return;
        }
        try
        {
            var payload = PcaSnapshotCodec.Serialize(BuildLocalSnapshot(), MaxBytes);
            submits.Sent();
            Send(clientRpc, MessageKind.Submit, payload);
            Diag(PcaDiagnosticLevel.Verbose, clientRpc, "SUBMIT_QUEUED", "reason=" + reason + " pending=" + submits.Pending + " bytes=" + payload.Length);
        }
        catch (Exception ex)
        {
            submits.Acknowledge();
            Warn("Could not submit " + reason + " snapshot: " + ex.Message);
        }
    }

    public static void SubmitCurrentSnapshot() => Instance?.SubmitLocalSnapshot("character state change");

    private CharacterSnapshot BuildLocalSnapshot()
    {
        var profile = Game.instance?.GetPlayerProfile() ?? throw new InvalidOperationException("Player profile is unavailable.");
        // EpicMMO level, XP and attributes live in Player.m_knownTexts and are serialized by this native call.
        if (Player.m_localPlayer != null)
        {
            profile.SavePlayerData(Player.m_localPlayer);
            Minimap.instance?.SaveMapData();
            profile.SaveLogoutPoint();
        }
        var data = AccessTools.Field(typeof(PlayerProfile), "m_playerData").GetValue(profile) as byte[];
        if (data == null) throw new InvalidDataException("Native player data is unavailable.");
        var snapshot = new CharacterSnapshot
        {
            // The server replaces this untrusted marker with the authenticated session account.
            AccountId = "client-untrusted",
            CharacterId = profile.GetPlayerID(),
            CharacterName = profile.GetName(),
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
            UpdatedUtcTicks = DateTime.UtcNow.Ticks,
            VanillaPlayerData = data
        };
        var vl = VLBridge.Export(Player.m_localPlayer);
        if (vl != null) snapshot.Extensions.Add(VLExtension, vl);
        var worldProfile = WorldProfileBridge.Export(profile, MaxBytes);
        if (worldProfile != null) snapshot.Extensions.Add(WorldProfileExtension, worldProfile);
        if (freshProgressionApplied) snapshot.Extensions.Add(FreshAppliedExtension, new ExtensionPayload { SchemaVersion = 1, Data = Array.Empty<byte>() });
        return snapshot;
    }

    private bool TryGetAuthenticatedAccount(ZNet network, ZRpc rpc, out string accountId, out ZNetPeer peer, out string reason)
    {
        accountId = null;
        peer = null;
        foreach (var candidate in network.GetPeers())
            if (candidate.m_rpc == rpc) { peer = candidate; break; }
        if (peer == null) { reason = "peer_not_found"; return false; }
        if (!connections.TryGetValue(rpc, out var state) || !state.NativeNetworkAccepted) { reason = "native_peerinfo_not_accepted"; return false; }
        if (!rpc.IsConnected()) { reason = "rpc_disconnected"; return false; }
        // Networking mods may wrap ZRpc's socket after Valheim has accepted the peer.
        // Prefer the peer's native socket, which is still available in the AddPeer callback.
        var socket = peer.m_socket;
        if (!(socket is ZSteamSocket) && !(socket is ZPlayFabSocket))
        {
            var rpcSocket = rpc.GetSocket();
            if (rpcSocket is ZSteamSocket || rpcSocket is ZPlayFabSocket) socket = rpcSocket;
        }
        if (socket is ZSteamSocket steam) accountId = steam.GetHostName();
        else if (socket is ZPlayFabSocket playFab && !string.IsNullOrWhiteSpace(playFab.m_remotePlayerId)) accountId = "playfab/" + playFab.m_remotePlayerId;
        if (string.IsNullOrWhiteSpace(accountId)) { reason = socket == null ? "socket_missing" : "identity_unavailable_" + socket.GetType().Name; return false; }
        reason = "authenticated";
        return true;
    }

    private void EnsureRole(ZNet network)
    {
        if (role != "UNKNOWN" || network == null) return;
        role = network.IsServer() ? "SERVER" : "CLIENT";
        diagnostics?.DetectRole(role, network.IsDedicated());
    }

    private PcaServerConnectionState GetOrCreateConnection(ZRpc rpc)
    {
        if (connections.TryGetValue(rpc, out var state)) return state;
        state = new PcaServerConnectionState("C" + (++nextConnectionId).ToString("D3"), Time.realtimeSinceStartup);
        connections[rpc] = state;
        return state;
    }

    private void LogPendingClientState()
    {
        if (diagnostics.Level < PcaDiagnosticLevel.Trace) return;
        if (clientRpc == null || (handshake.State != PcaHandshakeState.WaitingForHello && handshake.State != PcaHandshakeState.WaitingForSnapshot)) return;
        var now = Time.realtimeSinceStartup;
        if (now - clientLastStateLogAt < diagnostics.PendingInterval) return;
        clientLastStateLogAt = now;
        Diag(PcaDiagnosticLevel.Trace, clientRpc, "STATE", "age=" + (now - clientHandshakeStartedAt).ToString("F1") + "s handshake=" + handshake.State +
            " rpcConnected=" + clientRpc.IsConnected() + " serverDetected=" + serverDetected + " canSpawn=" + handshake.CanSpawn + " pendingSubmits=" + submits.Pending);
    }

    private string ConnectionDetails(ZNet network, ZRpc rpc, PcaServerConnectionState state, float now)
    {
        ZNetPeer peer = null;
        foreach (var candidate in network.GetPeers()) if (candidate.m_rpc == rpc) { peer = candidate; break; }
        return "age=" + (now - state.StartedAt).ToString("F1") + "s " + PeerDetails(peer, state) + " peerInfoSeen=" + state.PeerInfoSeen +
            " authenticated=" + state.NativeAuthenticated + " helloSent=" + state.HelloSent + " identifyReceived=" + state.IdentifyReceived +
            " session=" + state.SessionAuthorized + " snapshotSent=" + state.SnapshotSent + " spawnAllowed=" + state.SessionAuthorized +
            " zdoPeerAdded=" + state.ZdoPeerAdded + " routedPeerAdded=" + state.RoutedPeerAdded;
    }

    private static string PeerDetails(ZNetPeer peer, PcaServerConnectionState state) =>
        "rpcHash=" + (peer?.m_rpc == null ? "none" : peer.m_rpc.GetHashCode().ToString("X8")) + " socket=" + (peer?.m_socket?.GetType().Name ?? "none") +
        " rpcConnected=" + (peer?.m_rpc?.IsConnected() ?? false) + " peerFound=" + (peer != null) + " peerReady=" + (peer?.IsReady() ?? false) +
        " peerUid=" + (peer?.m_uid ?? 0) + " playerNameKnown=" + !string.IsNullOrWhiteSpace(peer?.m_playerName) + " registered=" + state.RpcRegistered;

    private static string SnapshotDetails(CharacterSnapshot snapshot)
    {
        var extensions = new List<string>();
        foreach (var extension in snapshot.Extensions) extensions.Add(extension.Key + ":" + (extension.Value?.Data?.Length ?? 0));
        return "account=" + MaskAccount(snapshot.AccountId) + " characterId=" + snapshot.CharacterId + " vanillaBytes=" + (snapshot.VanillaPlayerData?.Length ?? 0) +
            " extensions=" + string.Join(",", extensions) + " created=" + snapshot.CreatedUtcTicks + " updated=" + snapshot.UpdatedUtcTicks;
    }

    private void DisconnectRejectedClient(string reason)
    {
        var network = ZNet.instance;
        if (network == null || network.IsServer()) return;
        pendingClientRejection = reason;
        ZNetPeer rejected = null;
        foreach (var peer in network.GetPeers())
            if (peer.m_rpc == clientRpc) { rejected = peer; break; }
        ZNet.SetExternalError(ZNet.ConnectionStatus.ErrorDisconnected);
        if (rejected != null) network.Disconnect(rejected);
        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, reason);
        Audit("Client disconnected: " + reason);
    }

    internal void ShowPendingClientRejection()
    {
        if (string.IsNullOrWhiteSpace(pendingClientRejection) || !UnifiedPopup.IsAvailable()) return;
        var message = pendingClientRejection;
        pendingClientRejection = null;
        UnifiedPopup.Push(new WarningPopup("Perspex Character Authority", message, UnifiedPopup.Pop, localizeText: false));
    }

    private void DisconnectPeer(ZRpc rpc)
    {
        var network = ZNet.instance;
        if (network == null) return;
        ZNetPeer target = null;
        foreach (var peer in network.GetPeers())
            if (peer.m_rpc == rpc) { target = peer; break; }
        Diag(PcaDiagnosticLevel.Basic, rpc, "DISCONNECT_REQUEST", "peerFound=" + (target != null));
        if (target != null) network.Disconnect(target);
    }

    internal void PeerDisconnected(ZRpc rpc)
    {
        if (rpc == null) return;
        registeredRpcs.Remove(rpc);
        authenticatedAccounts.Remove(rpc);
        if (connections.TryGetValue(rpc, out var connection))
            Diag(PcaDiagnosticLevel.Basic, rpc, "PEER_RELEASE", "finalState=" + connection.LastEvent + " authorized=" + connection.SessionAuthorized +
                " spawnAllowed=" + connection.SessionAuthorized);
        connections.Remove(rpc);
        if (!sessions.TryGetValue(rpc, out var session)) return;
        sessions.Remove(rpc);
        var key = session.AccountId + "\n" + session.CharacterId;
        if (activeCharacters.TryGetValue(key, out var owner) && owner == rpc) activeCharacters.Remove(key);
        Audit("Authorized session released for account=" + MaskAccount(session.AccountId) + " character=" + session.CharacterId + ".");
    }

    private void Send(ZRpc rpc, MessageKind kind, byte[] payload)
    {
        Diag(PcaDiagnosticLevel.Verbose, rpc, "SEND_" + kind, "protocol=" + ProtocolVersion + " bytes=" + (payload?.Length ?? 0) + " handshake=" + handshake.State);
        var package = new ZPackage();
        package.Write(ProtocolVersion);
        package.Write((int)kind);
        package.Write(payload ?? Array.Empty<byte>());
        rpc.Invoke(RpcName, package);
    }

    private void Deny(ZRpc rpc, string message)
    {
        Warn("Character rejected: " + message);
        Send(rpc, MessageKind.Denied, System.Text.Encoding.UTF8.GetBytes(message ?? "Character access denied."));
    }
    private void Diag(PcaDiagnosticLevel level, ZRpc rpc, string eventName, string details = "")
    {
        if (diagnostics == null || !diagnostics.Enabled) return;
        var connection = rpc != null && connections.TryGetValue(rpc, out var state) ? state.Id : "-";
        diagnostics.Log(level, role, connection, eventName, details);
    }
    internal void DiagExternal(string eventName, string details) => Diag(PcaDiagnosticLevel.Verbose, clientRpc, eventName, details);
    private void Audit(string message) => Logger.LogInfo("[PCA] " + message);
    private void Log(string message) { if (debugLogging.Value) Logger.LogInfo("[PCA] " + message); }
    private void Warn(string message) => Logger.LogWarning("[PCA] " + message);
    internal void WarnExternal(string message) => Warn(message);
    private static string MaskAccount(string account) => string.IsNullOrEmpty(account) || account.Length < 9 ? "<redacted>" : account.Substring(0, 4) + "..." + account.Substring(account.Length - 4);

    private void RunCommand(Terminal.ConsoleEventArgs args)
    {
        if (args.Length < 2) { Print(args, "pca: list|info|backup|backups|restore|reset|delete|export|binding|characters|unbind|bind|status|diag"); return; }
        var action = args[1].ToLowerInvariant();
        if (action == "status")
        {
            Print(args, "PCA=" + PluginVersion + " Enabled=" + Enabled + " Role=" + role +
                " Protocol=" + ProtocolVersion + " AuthorityRequired=" + requireServerAuthority.Value + " Handshake=" + handshake.State +
                " ServerDetected=" + serverDetected + " RPCs=" + registeredRpcs.Count + " Authenticated=" + authenticatedAccounts.Count +
                " Sessions=" + sessions.Count + " Account=" + StatusAccount() + " CharacterId=" + handshake.CharacterId +
                " Snapshot=" + (lastServerSnapshotTicks == 0 ? "none" : new DateTime(lastServerSnapshotTicks, DateTimeKind.Utc).ToString("O")));
            return;
        }
        if (action == "diag")
        {
            if (!RequireServer(args)) return;
            var requested = args.Length >= 3 && !string.Equals(args[2], "connections", StringComparison.OrdinalIgnoreCase) ? args[2] : null;
            Print(args, "PCA " + PluginVersion + " " + role + " diagnostics=" + diagnostics.Level + " connections=" + connections.Count);
            foreach (var pair in connections)
            {
                if (requested != null && !string.Equals(requested, pair.Value.Id, StringComparison.OrdinalIgnoreCase)) continue;
                Print(args, pair.Value.Id + " " + ConnectionDetails(ZNet.instance, pair.Key, pair.Value, Time.realtimeSinceStartup));
            }
            return;
        }
        if (!RequireServer(args)) return;
        if (action == "list") { foreach (var binding in store.GetAccountBindings()) Print(args, binding.AccountId + " -> " + binding.CharacterId + " " + binding.LastKnownCharacterName); return; }
        if (args.Length < 3) { Print(args, "Account ID is required."); return; }
        var account = args[2];
        if (action == "binding") { Print(args, store.TryGetBinding(account, out var binding) ? binding.CharacterId + " " + binding.LastKnownCharacterName : "No binding."); return; }
        if (action == "characters") { foreach (var item in store.GetCharacters(account)) Print(args, item.CharacterId + " " + item.CharacterName + " " + new DateTime(item.UpdatedUtcTicks, DateTimeKind.Utc)); return; }
        if (action == "info" && args.Length == 3)
        {
            Print(args, store.TryGetBinding(account, out var binding) ? "Binding: " + binding.CharacterId + " " + binding.LastKnownCharacterName : "No binding.");
            foreach (var item in store.GetCharacters(account)) Print(args, item.CharacterId + " " + item.CharacterName + " " + new DateTime(item.UpdatedUtcTicks, DateTimeKind.Utc));
            return;
        }
        if (action == "unbind") { Print(args, store.TryUnbind(account) ? "Binding removed; snapshots retained." : "No binding."); return; }
        if (action == "bind" && args.Length >= 4 && long.TryParse(args[3], out var bindId)) { Print(args, store.TryBind(account, bindId, args.Length >= 5 ? args[4] : string.Empty, out var err) ? "Binding saved." : err); return; }
        if (args.Length < 4 || !long.TryParse(args[3], out var characterId)) { Print(args, "CharacterId is required."); return; }
        if (action == "backups") { foreach (var backup in store.GetBackups(account, characterId)) Print(args, backup); return; }
        if (action == "backup") { Print(args, store.TryBackup(account, characterId, out var err) ? "Backup created." : err); return; }
        if (action == "restore" && args.Length >= 5) { var ok = store.TryRestore(account, characterId, args[4], out var err); if (ok) RevokeCharacter(account, characterId); Print(args, ok ? "Restored with pre-restore backup." : err); return; }
        if (action == "reset") { var ok = ResetSnapshot(account, characterId, out var err); if (ok) RevokeCharacter(account, characterId); Print(args, ok ? "Fresh progression scheduled after backup." : err); return; }
        if (action == "delete") { var ok = store.TryDelete(account, characterId, out var err); if (ok) RevokeCharacter(account, characterId); Print(args, ok ? "Snapshot deleted after backup." : err); return; }
        if (action == "export") { var path = store.Export(account, characterId); Print(args, path ?? "Snapshot not found."); return; }
        if (action == "info") { if (store.TryRead(account, characterId, out var item, out var recovered)) Print(args, item.CharacterName + " " + item.UpdatedUtcTicks + (recovered ? " recovered-backup" : "")); else Print(args, "Snapshot not found."); return; }
        Print(args, "Invalid PCA command.");
    }

    private string StatusAccount()
    {
        if (clientSnapshot != null) return MaskAccount(clientSnapshot.AccountId);
        foreach (var session in sessions.Values) return MaskAccount(session.AccountId);
        return "none";
    }

    private bool ResetSnapshot(string account, long characterId, out string error)
    {
        if (!store.TryRead(account, characterId, out var snapshot, out var recovered)) { error = "Snapshot not found."; return false; }
        if (recovered && !store.TrySave(snapshot, out error)) return false;
        if (!store.TryBackup(account, characterId, out error)) return false;
        snapshot.Extensions[VLExtension] = VLBridge.EmptyState();
        snapshot.Extensions[FreshProgressionExtension] = new ExtensionPayload { SchemaVersion = 1, Data = Array.Empty<byte>() };
        snapshot.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
        return store.TrySave(snapshot, out error);
    }

    private void RevokeCharacter(string account, long characterId)
    {
        activeCharacters.Remove(account + "\n" + characterId);
        foreach (var pair in new List<KeyValuePair<ZRpc, Session>>(sessions))
        {
            if (!string.Equals(pair.Value.AccountId, account, StringComparison.Ordinal) || pair.Value.CharacterId != characterId) continue;
            sessions.Remove(pair.Key);
            Deny(pair.Key, "Character authority was changed by an administrator; reconnect required.");
            pair.Key.Invoke("Error", 8);
        }
    }

    private static bool RequireServer(Terminal.ConsoleEventArgs args)
    {
        if (ZNet.instance != null && ZNet.instance.IsServer()) return true;
        Print(args, "PCA administration requires the server console.");
        return false;
    }
    private static void Print(Terminal.ConsoleEventArgs args, string value) => args.Context?.AddString(value);

    [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
    private static class ZNetNewConnectionPatch
    {
        private static void Prefix(ZNet __instance, ZNetPeer peer) => Instance?.OnNewConnection(__instance, peer);
        private static void Postfix(ZNet __instance, ZNetPeer peer) => Instance?.OnNewConnectionExit(__instance, peer);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_ServerHandshake")]
    private static class ZNetServerHandshakePatch
    {
        private static void Prefix(ZNet __instance, ZRpc rpc) => Instance?.NativeHandshakeEvent(__instance, rpc, "RPC_SERVER_HANDSHAKE_ENTER");
        private static void Postfix(ZNet __instance, ZRpc rpc) => Instance?.NativeHandshakeEvent(__instance, rpc, "RPC_SERVER_HANDSHAKE_EXIT");
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
    private static class ZNetPeerInfoPatch
    {
        private static void Prefix(ZNet __instance, ZRpc rpc) => Instance?.OnPeerInfoEnter(__instance, rpc);
        private static void Postfix(ZNet __instance, ZRpc rpc) => Instance?.OnPeerInfo(__instance, rpc);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_PlayerID")]
    private static class ZNetPlayerIdPatch
    {
        private static bool Prefix(ZNet __instance, ZRpc rpc, long playerID) =>
            !__instance.IsServer() || Instance == null || Instance.AuthorizeNativePlayerId(rpc, playerID);
    }

    [HarmonyPatch(typeof(ZNet), "RPC_CharacterID")]
    private static class ZNetCharacterIdPatch
    {
        private static bool Prefix(ZNet __instance, ZRpc rpc, ZDOID characterID) =>
            !__instance.IsServer() || Instance == null || Instance.AuthorizeNativeCharacterId(rpc, characterID);
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.Disconnect), new[] { typeof(ZNetPeer) })]
    private static class ZNetDisconnectPatch
    {
        private static void Prefix(ZNet __instance, ZNetPeer peer)
        {
            Instance?.NativeHandshakeEvent(__instance, peer?.m_rpc, "DISCONNECT_NATIVE");
            Instance?.PeerDisconnected(peer?.m_rpc);
        }
    }

    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.AddPeer))]
    private static class ZdoAddPeerPatch { private static void Prefix(ZNetPeer netPeer) => Instance?.ObserveNativeAddPeer(netPeer, true); }

    [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.AddPeer))]
    private static class RoutedAddPeerPatch { private static void Prefix(ZNetPeer peer) => Instance?.ObserveNativeAddPeer(peer, false); }

    [HarmonyPatch(typeof(Game), "_RequestRespawn")]
    private static class GameRequestRespawnPatch { private static bool Prefix() => Instance == null || !Instance.BlockRespawn(); }

    [HarmonyPatch(typeof(Game), "SpawnPlayer")]
    private static class GameSpawnPlayerPatch { private static bool Prefix() => Instance == null || !Instance.BlockPlayerSpawn(); }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    [HarmonyPriority(Priority.First)]
    private static class PlayerSpawnPatch
    {
        private static void Prefix(Player __instance) => Instance?.ApplyPendingVLExtension(__instance);
    }

    [HarmonyPatch(typeof(PlayerProfile), "Save")]
    private static class PlayerProfileSavePatch { private static void Postfix() => Instance?.SubmitLocalSnapshot("player save"); }

    [HarmonyPatch(typeof(PlayerProfile), "LoadPlayerData")]
    [HarmonyPriority(Priority.Last)]
    private static class PlayerProfileLoadPatch { private static void Postfix(PlayerProfile __instance, Player player) => Instance?.ApplyFreshProgression(__instance, player); }

    [HarmonyPatch(typeof(Game), "Logout")]
    private static class GameLogoutPatch
    {
        private static void Prefix() { if (Instance != null && Instance.saveOnLogout.Value) Instance.SubmitLocalSnapshot("logout"); }
    }

    [HarmonyPatch(typeof(FejdStartup), "Start")]
    private static class FejdStartupPatch { private static void Postfix() => Instance?.ShowPendingClientRejection(); }

    [HarmonyPatch(typeof(ZNet), "OnDestroy")]
    private static class ZNetDestroyPatch
    {
        private static void Prefix(ZNet __instance)
        {
            if (Instance == null) return;
            if (Instance.saveOnDisconnect.Value) Instance.SubmitLocalSnapshot("disconnect");
            Instance.ResetClientSession(__instance);
        }
    }
}

internal static class WorldProfileBridge
{
    private static readonly MethodInfo GetWorldData = AccessTools.Method(typeof(PlayerProfile), "GetWorldData");

    internal static ExtensionPayload Export(PlayerProfile profile, int maxBytes)
    {
        try
        {
            var worldId = ZNet.instance?.GetWorldUID() ?? 0;
            var data = worldId == 0 ? null : GetWorldData?.Invoke(profile, new object[] { worldId });
            if (data == null) return null;
            var type = data.GetType();
            var spawn = (Vector3)AccessTools.Field(type, "m_spawnPoint").GetValue(data);
            var logout = (Vector3)AccessTools.Field(type, "m_logoutPoint").GetValue(data);
            var death = (Vector3)AccessTools.Field(type, "m_deathPoint").GetValue(data);
            var home = (Vector3)AccessTools.Field(type, "m_homePoint").GetValue(data);
            var state = new WorldProfileState
            {
                WorldId = worldId,
                HaveCustomSpawnPoint = (bool)AccessTools.Field(type, "m_haveCustomSpawnPoint").GetValue(data),
                SpawnX = spawn.x, SpawnY = spawn.y, SpawnZ = spawn.z,
                HaveLogoutPoint = (bool)AccessTools.Field(type, "m_haveLogoutPoint").GetValue(data),
                LogoutX = logout.x, LogoutY = logout.y, LogoutZ = logout.z,
                HaveDeathPoint = (bool)AccessTools.Field(type, "m_haveDeathPoint").GetValue(data),
                DeathX = death.x, DeathY = death.y, DeathZ = death.z,
                HomeX = home.x, HomeY = home.y, HomeZ = home.z,
                MapData = AccessTools.Field(type, "m_mapData").GetValue(data) as byte[]
            };
            return new ExtensionPayload { SchemaVersion = PcaWorldProfileCodec.SchemaVersion, Data = PcaWorldProfileCodec.Serialize(state, maxBytes) };
        }
        catch (Exception ex)
        {
            PerspexCharacterAuthorityPlugin.Instance?.WarnExternal("World profile export failed: " + ex.Message);
            return null;
        }
    }

    internal static void Import(PlayerProfile profile, ExtensionPayload payload, int maxBytes)
    {
        try
        {
            if (payload?.SchemaVersion != PcaWorldProfileCodec.SchemaVersion ||
                !PcaWorldProfileCodec.TryDeserialize(payload.Data, maxBytes, out var state)) return;
            var data = GetWorldData?.Invoke(profile, new object[] { state.WorldId });
            if (data == null) return;
            var type = data.GetType();
            AccessTools.Field(type, "m_haveCustomSpawnPoint").SetValue(data, state.HaveCustomSpawnPoint);
            AccessTools.Field(type, "m_spawnPoint").SetValue(data, new Vector3(state.SpawnX, state.SpawnY, state.SpawnZ));
            AccessTools.Field(type, "m_haveLogoutPoint").SetValue(data, state.HaveLogoutPoint);
            AccessTools.Field(type, "m_logoutPoint").SetValue(data, new Vector3(state.LogoutX, state.LogoutY, state.LogoutZ));
            AccessTools.Field(type, "m_haveDeathPoint").SetValue(data, state.HaveDeathPoint);
            AccessTools.Field(type, "m_deathPoint").SetValue(data, new Vector3(state.DeathX, state.DeathY, state.DeathZ));
            AccessTools.Field(type, "m_homePoint").SetValue(data, new Vector3(state.HomeX, state.HomeY, state.HomeZ));
            AccessTools.Field(type, "m_mapData").SetValue(data, state.MapData);
        }
        catch (Exception ex) { PerspexCharacterAuthorityPlugin.Instance?.WarnExternal("World profile import failed: " + ex.Message); }
    }
}

