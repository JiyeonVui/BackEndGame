public class InMemoryRealtimeConnectionTracker : IRealtimeConnectionTracker
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, HashSet<string>> _connectionsByDeviceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _deviceIdByConnectionId = new(StringComparer.OrdinalIgnoreCase);

    public RealtimeConnectionChange RegisterConnection(string deviceId, string connectionId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        var normalizedConnectionId = NormalizeConnectionId(connectionId);

        lock (_syncRoot)
        {
            if (_deviceIdByConnectionId.TryGetValue(normalizedConnectionId, out var currentDeviceId))
            {
                return BuildChange(currentDeviceId, 
                    normalizedConnectionId, 
                    GetActiveConnectionCount(currentDeviceId), 
                    false, 
                    false
                    );
            }

            if (!_connectionsByDeviceId.TryGetValue(normalizedDeviceId, out var connections))
            {
                connections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _connectionsByDeviceId[normalizedDeviceId] = connections;
            }

            var becameOnline = connections.Count == 0;
            connections.Add(normalizedConnectionId);
            _deviceIdByConnectionId[normalizedConnectionId] = normalizedDeviceId;

            return BuildChange(normalizedDeviceId, normalizedConnectionId, connections.Count, becameOnline, false);
        }
    }

    public RealtimeConnectionChange? UnregisterConnection(string connectionId)
    {
        var normalizedConnectionId = NormalizeConnectionId(connectionId);

        lock (_syncRoot)
        {
            if (!_deviceIdByConnectionId.TryGetValue(normalizedConnectionId, out var deviceId))
            {
                return null;
            }

            _deviceIdByConnectionId.Remove(normalizedConnectionId);

            if (!_connectionsByDeviceId.TryGetValue(deviceId, out var connections))
            {
                return BuildChange(deviceId, normalizedConnectionId, 0, false, true);
            }

            connections.Remove(normalizedConnectionId);
            var activeConnections = connections.Count;
            var becameOffline = activeConnections == 0;

            if (becameOffline)
            {
                _connectionsByDeviceId.Remove(deviceId);
            }

            return BuildChange(deviceId, normalizedConnectionId, activeConnections, false, becameOffline);
        }
    }

    public bool IsOnline(string deviceId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);

        lock (_syncRoot)
        {
            return _connectionsByDeviceId.TryGetValue(normalizedDeviceId, out var connections) &&
                   connections.Count > 0;
        }
    }

    public int GetConnectionCount(string deviceId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);

        lock (_syncRoot)
        {
            return GetActiveConnectionCount(normalizedDeviceId);
        }
    }

    public string? GetDeviceId(string connectionId)
    {
        var normalizedConnectionId = NormalizeConnectionId(connectionId);

        lock (_syncRoot)
        {
            return _deviceIdByConnectionId.TryGetValue(normalizedConnectionId, out var deviceId)
                ? deviceId
                : null;
        }
    }

    private static RealtimeConnectionChange BuildChange(
        string deviceId,
        string connectionId,
        int activeConnections,
        bool becameOnline,
        bool becameOffline)
    {
        return new RealtimeConnectionChange
        {
            DeviceId = deviceId,
            ConnectionId = connectionId,
            ActiveConnections = activeConnections,
            BecameOnline = becameOnline,
            BecameOffline = becameOffline
        };
    }

    private int GetActiveConnectionCount(string deviceId)
    {
        return _connectionsByDeviceId.TryGetValue(deviceId, out var connections)
            ? connections.Count
            : 0;
    }

    private static string NormalizeDeviceId(string deviceId)
    {
        return deviceId.Trim();
    }

    private static string NormalizeConnectionId(string connectionId)
    {
        return connectionId.Trim();
    }
}
