public interface IRealtimeConnectionTracker
{
    RealtimeConnectionChange RegisterConnection(string deviceId, string connectionId);
    RealtimeConnectionChange? UnregisterConnection(string connectionId);
    bool IsOnline(string deviceId);
    int GetConnectionCount(string deviceId);
    string? GetDeviceId(string connectionId);
}
