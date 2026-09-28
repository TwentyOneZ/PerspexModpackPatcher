namespace PerspexCharacterAuthority;

public sealed class PcaServerConnectionState
{
    public PcaServerConnectionState(string id, float startedAt)
    {
        Id = id;
        StartedAt = startedAt;
        LastStateLogAt = startedAt;
    }

    public string Id { get; }
    public float StartedAt { get; }
    public float PeerInfoAt { get; private set; }
    public float LastStateLogAt { get; set; }
    public string LastEvent { get; private set; } = "Connected";
    public bool RpcRegistered { get; set; }
    public bool PeerInfoSeen { get; private set; }
    public bool NativeAuthenticated { get; private set; }
    public bool HelloSent { get; private set; }
    public bool IdentifyReceived { get; private set; }
    public bool SessionAuthorized { get; private set; }
    public bool SnapshotSent { get; private set; }
    public bool ZdoPeerAdded { get; private set; }
    public bool RoutedPeerAdded { get; private set; }
    public bool NativeNetworkAccepted => ZdoPeerAdded;

    public void MarkPeerInfo(float now)
    {
        if (!PeerInfoSeen) PeerInfoAt = now;
        PeerInfoSeen = true;
        LastEvent = "PeerInfoSeen";
    }
    public void MarkAuthenticated() { NativeAuthenticated = true; LastEvent = "NativeAuthenticated"; }
    public void MarkZdoPeerAdded() { ZdoPeerAdded = true; LastEvent = "NativeNetworkAccepted"; }
    public void MarkRoutedPeerAdded() { RoutedPeerAdded = true; }
    public bool TryMarkHelloSent()
    {
        if (!NativeAuthenticated || HelloSent) return false;
        HelloSent = true;
        LastEvent = "HelloSent";
        return true;
    }
    public void MarkIdentify() { IdentifyReceived = true; LastEvent = "IdentifyReceived"; }
    public void MarkAuthorized() { SessionAuthorized = true; LastEvent = "Authorized"; }
    public void MarkSnapshotSent() { SnapshotSent = true; LastEvent = "SnapshotSent"; }
    public string TimeoutPhase => !NativeAuthenticated ? "native-authentication" : !IdentifyReceived ? "identify" : !SessionAuthorized ? "character-authorization" : "complete";
}

public sealed class PcaSubmitTracker
{
    public int Pending { get; private set; }
    public void Sent() => Pending++;
    public bool Acknowledge()
    {
        if (Pending == 0) return false;
        Pending--;
        return true;
    }
    public void Reset() => Pending = 0;
}
