public class RealtimeConnectionChange
{
    public string DeviceId { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public int ActiveConnections { get; init; }
    public bool BecameOnline { get; init; }
    public bool BecameOffline { get; init; }
}
