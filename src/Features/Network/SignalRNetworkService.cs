using System.Collections.Concurrent;
using BackEndGame.Domain.Packets;
using BackEndGame.Game;
using Microsoft.AspNetCore.SignalR;

namespace BackEndGame.Features.Network
{
    /// <summary>
    /// SignalR-based implementation of INetworkService.
    ///
    /// Architecture notes:
    ///   - Uses a dedicated SignalR Hub (GameHub) for game packet traffic,
    ///     separate from PresenceHub which handles online/offline presence only.
    ///   - Each match gets a SignalR Group named "match:{matchId}" so BroadcastGameStateAsync
    ///     can fan out to all 10 players with a single hub call.
    ///   - InputPackets are received as hub method calls from Unity clients and
    ///     forwarded to IMatchManager via the registered OnInputReceived handler.
    /// </summary>
    public class SignalRNetworkService : INetworkService
    {
        // IHubContext lets us push messages to clients from outside a Hub class.
        private readonly IHubContext<GameHub> _hubContext;
        private readonly ILogger<SignalRNetworkService> _logger;

        private Action<Guid, InputPacket>? _inputHandler;

        // GameHub connection tracking: deviceId ↔ connectionId
        private readonly ConcurrentDictionary<string, string> _deviceToConnection = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _connectionToDevice = new(StringComparer.OrdinalIgnoreCase);

        // Match group tracking: matchId → connectionId set, connectionId → matchId
        private readonly ConcurrentDictionary<Guid, HashSet<string>> _matchConnections = new();
        private readonly ConcurrentDictionary<string, Guid> _connectionToMatch = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _matchConnectionsLock = new();

        public SignalRNetworkService(IHubContext<GameHub> hubContext, ILogger<SignalRNetworkService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        // ─── INetworkService ──────────────────────────────────────────────────────

        /// <summary>
        /// Calls _hubContext.Clients.Group("match:{matchId}").SendAsync("game:state", packet).
        /// SignalR serialises the struct and delivers it to all clients in the group.
        /// This is invoked ~64 times per second per match, so serialisation overhead matters —
        /// consider MessagePack (binary) over JSON for production.
        /// </summary>
        public async Task BroadcastGameStateAsync(Guid matchId, GameStatePacket packet)
        {
            await _hubContext.Clients
                .Group(MatchGroup(matchId))
                .SendAsync("game:state", packet);
        }

        /// <summary>
        /// Looks up the player's active SignalR ConnectionId from IRealtimeConnectionTracker,
        /// then calls _hubContext.Clients.Client(connectionId).SendAsync("game:state", packet).
        /// </summary>
        public async Task SendGameStateToPlayerAsync(string playerDeviceId, GameStatePacket packet)
        {
            if (!_deviceToConnection.TryGetValue(playerDeviceId, out var connectionId))
                return;

            await _hubContext.Clients.Client(connectionId).SendAsync("game:state", packet);
        }

        /// <summary>
        /// Stores the handler reference. GameHub.ReceiveInput() will call this handler
        /// when a client sends an input packet over the WebSocket.
        /// </summary>
        public void OnInputReceived(Action<Guid, InputPacket> handler)
        {
            _inputHandler = handler;
        }

        /// <summary>
        /// Signal 1 of 2 — triggers Home → Loading screen on each client.
        /// Sends "match:found" directly to each player's connection (not via group yet).
        /// Payload: { matchId, teamId } so the loading screen knows team assignment.
        /// </summary>
        public async Task NotifyMatchFoundAsync(Guid matchId, List<string> team0DeviceIds, List<string> team1DeviceIds)
        {
            var tasks = new List<Task>();

            for (int i = 0; i < team0DeviceIds.Count; i++)
            {
                var deviceId = team0DeviceIds[i];
                if (!_deviceToConnection.TryGetValue(deviceId, out var connectionId)) continue;
                tasks.Add(_hubContext.Clients.Client(connectionId).SendAsync("match:found", new
                {
                    matchId,
                    teamId = 0,
                    playerId = (byte)i
                }));
            }

            for (int i = 0; i < team1DeviceIds.Count; i++)
            {
                var deviceId = team1DeviceIds[i];
                if (!_deviceToConnection.TryGetValue(deviceId, out var connectionId)) continue;
                tasks.Add(_hubContext.Clients.Client(connectionId).SendAsync("match:found", new
                {
                    matchId,
                    teamId = 1,
                    playerId = (byte)(team0DeviceIds.Count + i)
                }));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Signal 2 of 2 — triggers Loading → Game screen on each client.
        /// Adds each player's ConnectionId to the SignalR group "match:{matchId}"
        /// so future BroadcastGameStateAsync calls reach all of them at once.
        /// Then sends a "match:started" event confirming the loop is live.
        /// </summary>
        public async Task NotifyMatchStartedAsync(Guid matchId, IEnumerable<string> playerDeviceIds)
        {
            var tasks = new List<Task>();
            var group = MatchGroup(matchId);

            foreach (var deviceId in playerDeviceIds)
            {
                if (!_deviceToConnection.TryGetValue(deviceId, out var connectionId))
                {
                    _logger.LogWarning("[SignalR] NotifyMatchStarted — deviceId={DeviceId} not found in connection map, skipping", deviceId);
                    continue;
                }

                _logger.LogInformation("[SignalR] NotifyMatchStarted — adding deviceId={DeviceId} connId={ConnId} to group={Group}",
                    deviceId, connectionId, group);

                lock (_matchConnectionsLock)
                {
                    if (!_matchConnections.TryGetValue(matchId, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        _matchConnections[matchId] = set;
                    }
                    set.Add(connectionId);
                    _connectionToMatch[connectionId] = matchId;
                }

                tasks.Add(_hubContext.Groups.AddToGroupAsync(connectionId, group));
                tasks.Add(_hubContext.Clients.Client(connectionId).SendAsync("match:started", matchId));
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Sends a "match:ended" event to the match SignalR group carrying the MatchResult value.
        /// Then removes all players from the group to clean up SignalR group membership.
        /// </summary>
        public async Task NotifyMatchEndedAsync(Guid matchId, MatchResult result)
        {
            var group = MatchGroup(matchId);
            await _hubContext.Clients.Group(group).SendAsync("match:ended", result.ToString());

            HashSet<string>? connections;
            lock (_matchConnectionsLock)
            {
                _matchConnections.TryRemove(matchId, out connections);
            }

            if (connections == null) return;

            var removeTasks = connections.Select(connId =>
            {
                _connectionToMatch.TryRemove(connId, out _);
                return _hubContext.Groups.RemoveFromGroupAsync(connId, group);
            });

            await Task.WhenAll(removeTasks);
        }

        // ─── GameHub connection tracking ──────────────────────────────────────────

        public void RegisterGameHubConnection(string deviceId, string connectionId)
        {
            _deviceToConnection[deviceId] = connectionId;
            _connectionToDevice[connectionId] = deviceId;
        }

        public void UnregisterGameHubConnection(string connectionId)
        {
            if (_connectionToDevice.TryRemove(connectionId, out var deviceId))
                _deviceToConnection.TryRemove(deviceId, out _);
        }

        public Guid? GetMatchIdForConnection(string connectionId)
        {
            return _connectionToMatch.TryGetValue(connectionId, out var matchId) ? matchId : null;
        }

        /// <summary>
        /// Removes a connection from match tracking and returns the number of players
        /// still connected to that match. Returns -1 if the connection wasn't in any match.
        /// Called on disconnect to decide whether to destroy the match.
        /// </summary>
        public int RemoveConnectionFromMatch(string connectionId)
        {
            if (!_connectionToMatch.TryRemove(connectionId, out var matchId))
                return -1;

            lock (_matchConnectionsLock)
            {
                if (_matchConnections.TryGetValue(matchId, out var set))
                {
                    set.Remove(connectionId);
                    return set.Count;
                }
            }
            return 0;
        }

        public void InvokeInputHandler(Guid matchId, InputPacket input)
        {
            _inputHandler?.Invoke(matchId, input);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static string MatchGroup(Guid matchId) => $"match:{matchId}";
    }
}
