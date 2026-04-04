public class ConnectionStatusResponse
{
    public string DeviceId { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public int ActiveConnections { get; init; }
}
