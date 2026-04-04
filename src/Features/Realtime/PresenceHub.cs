using BackEndGame.Infrastructure.Repositories;
using Microsoft.AspNetCore.SignalR;

public class PresenceHub : Hub
{
    private readonly IUserRepository _userRepository;
    private readonly IRealtimeConnectionTracker _connectionTracker;

    public PresenceHub(IUserRepository userRepository, IRealtimeConnectionTracker connectionTracker)
    {
        _userRepository = userRepository;
        _connectionTracker = connectionTracker;
    }

    public override async Task OnConnectedAsync()
    {
        var deviceId = NormalizeDeviceId(Context.GetHttpContext()?.Request.Query["deviceId"]);

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Context.Abort();
            return;
        }

        var user = await _userRepository.GetByDeviceIdAsync(deviceId);
        if (user == null)
        {
            Context.Abort();
            return;
        }

        var change = _connectionTracker.RegisterConnection(user.DeviceId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, GetDeviceGroup(user.DeviceId));

        await Clients.Caller.SendAsync("connection:ready", new
        {
            user.Id,
            user.DeviceId,
            user.UserName,
            ConnectionId = change.ConnectionId,
            change.ActiveConnections,
            ConnectedAt = DateTime.UtcNow
        });

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var change = _connectionTracker.UnregisterConnection(Context.ConnectionId);
        if (change != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetDeviceGroup(change.DeviceId));
        }

        await base.OnDisconnectedAsync(exception);
    }

    public Task<object> GetConnectionState()
    {
        var deviceId = _connectionTracker.GetDeviceId(Context.ConnectionId);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            throw new HubException("Current connection is not bound to a device.");
        }

        return Task.FromResult<object>(new
        {
            DeviceId = deviceId,
            ConnectionId = Context.ConnectionId,
            ActiveConnections = _connectionTracker.GetConnectionCount(deviceId),
            IsOnline = _connectionTracker.IsOnline(deviceId)
        });
    }

    private static string GetDeviceGroup(string deviceId)
    {
        return $"device:{deviceId}";
    }

    private static string NormalizeDeviceId(string? deviceId)
    {
        return deviceId?.Trim() ?? string.Empty;
    }
}
