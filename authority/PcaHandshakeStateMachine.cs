using System;
using System.Collections.Generic;

namespace PerspexCharacterAuthority;

/// <summary>Protocol decisions with no dependency on Valheim or Unity.</summary>
public enum PcaHandshakeState
{
    Vanilla,
    WaitingForHello,
    WaitingForSnapshot,
    Allowed,
    Denied
}

public enum PcaHandshakeAction
{
    None,
    SendIdentify,
    AllowSpawn,
    Deny,
    VanillaFallback
}

public sealed class PcaHandshakeStateMachine
{
    private readonly int protocolVersion;
    private readonly bool authorityRequired;
    private long characterId;

    public PcaHandshakeStateMachine(int protocolVersion, bool authorityRequired)
    {
        this.protocolVersion = protocolVersion;
        this.authorityRequired = authorityRequired;
    }

    public PcaHandshakeState State { get; private set; } = PcaHandshakeState.Vanilla;
    public long CharacterId => characterId;
    public bool CanSpawn => State == PcaHandshakeState.Allowed || State == PcaHandshakeState.Vanilla;
    public bool CanSubmit => State == PcaHandshakeState.Allowed;

    public void Begin(long selectedCharacterId)
    {
        characterId = selectedCharacterId;
        State = PcaHandshakeState.WaitingForHello;
    }

    public PcaHandshakeAction ReceiveHello(int protocol)
    {
        if (State != PcaHandshakeState.WaitingForHello) return PcaHandshakeAction.None;
        if (protocol != protocolVersion) return Deny();
        State = PcaHandshakeState.WaitingForSnapshot;
        return PcaHandshakeAction.SendIdentify;
    }

    public PcaHandshakeAction ReceiveSnapshot(int protocol, long snapshotCharacterId)
    {
        if (State != PcaHandshakeState.WaitingForSnapshot || protocol != protocolVersion || snapshotCharacterId != characterId)
            return Deny();
        State = PcaHandshakeState.Allowed;
        return PcaHandshakeAction.AllowSpawn;
    }

    public PcaHandshakeAction ReceiveDenied()
    {
        State = PcaHandshakeState.Denied;
        return PcaHandshakeAction.Deny;
    }

    public PcaHandshakeAction Timeout()
    {
        if (State != PcaHandshakeState.WaitingForHello && State != PcaHandshakeState.WaitingForSnapshot)
            return PcaHandshakeAction.None;
        if (authorityRequired || State == PcaHandshakeState.WaitingForSnapshot) return Deny();
        State = PcaHandshakeState.Vanilla;
        return PcaHandshakeAction.VanillaFallback;
    }

    public void Reset()
    {
        characterId = 0;
        State = PcaHandshakeState.Vanilla;
    }

    private PcaHandshakeAction Deny()
    {
        State = PcaHandshakeState.Denied;
        return PcaHandshakeAction.Deny;
    }
}

/// <summary>Keeps a replaced connection from submitting to the new session.</summary>
public sealed class PcaServerSessionGate<TPeer>
{
    private readonly Dictionary<TPeer, PcaServerSession> sessions = new Dictionary<TPeer, PcaServerSession>();
    private readonly Dictionary<string, TPeer> owners = new Dictionary<string, TPeer>(StringComparer.Ordinal);

    public bool Authorize(TPeer peer, string accountId, long characterId, out TPeer replacedPeer)
    {
        var key = Key(accountId, characterId);
        if (owners.TryGetValue(key, out replacedPeer) && !EqualityComparer<TPeer>.Default.Equals(replacedPeer, peer))
            sessions.Remove(replacedPeer);
        else
            replacedPeer = default;
        owners[key] = peer;
        sessions[peer] = new PcaServerSession(accountId, characterId);
        return true;
    }

    public bool CanUsePlayerId(TPeer peer, long playerId) =>
        sessions.TryGetValue(peer, out var session) && session.CharacterId == playerId;

    public bool CanSubmit(TPeer peer, long characterId) => CanUsePlayerId(peer, characterId);

    public void Revoke(TPeer peer)
    {
        if (!sessions.TryGetValue(peer, out var session)) return;
        sessions.Remove(peer);
        var key = Key(session.AccountId, session.CharacterId);
        if (owners.TryGetValue(key, out var owner) && EqualityComparer<TPeer>.Default.Equals(owner, peer)) owners.Remove(key);
    }

    private static string Key(string accountId, long characterId) => accountId + "\n" + characterId;

    private readonly struct PcaServerSession
    {
        public PcaServerSession(string accountId, long characterId) { AccountId = accountId; CharacterId = characterId; }
        public string AccountId { get; }
        public long CharacterId { get; }
    }
}

/// <summary>Tracks an action that must run once after each load.</summary>
public sealed class PcaPendingActionGate
{
    public bool Pending { get; private set; }

    public void Load(bool requested) => Pending = requested;

    public bool TryApply()
    {
        if (!Pending) return false;
        Pending = false;
        return true;
    }
}
