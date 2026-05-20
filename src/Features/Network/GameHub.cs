using BackEndGame.Domain.Packets;
using BackEndGame.Features.GameLoop;
using BackEndGame.Game;
using Microsoft.AspNetCore.SignalR;

namespace BackEndGame.Features.Network
{
    /// <summary>
    /// SignalR Hub dedicated to in-game packet traffic.
    /// Unity clients connect here once a match starts.
    ///
    /// Client → Server methods (called by Unity via SignalR invoke):
    ///   - SendInput: delivers an InputPacket for the current tick.
    ///
    /// Server → Client events (pushed by SignalRNetworkService):
    ///   - game:state    — GameStatePacket broadcast every tick.
    ///   - match:started — sent once when a match is ready.
    ///   - match:ended   — sent once when a match concludes.
    /// </summary>
    public class GameHub : Hub
    {
        private readonly IMatchManager _matchManager;
        private readonly SignalRNetworkService _networkService;
        private readonly GameLoopService _gameLoopService;
        private readonly ILogger<GameHub> _logger;

        private const int MatchmakingDelayMs = 5000;

        public GameHub(IMatchManager matchManager, SignalRNetworkService networkService, GameLoopService gameLoopService, ILogger<GameHub> logger)
        {
            _matchManager = matchManager;
            _networkService = networkService;
            _gameLoopService = gameLoopService;
            _logger = logger;
        }

        /// <summary>
        /// Called by the Unity client to enter matchmaking queue.
        /// Returns immediately; match:found + match:started are pushed via SignalR once ready.
        /// </summary>
        public Task FindGame(string deviceId)
        {
            _logger.LogInformation("[GameHub] FindGame called by deviceId={DeviceId} connId={ConnId}", deviceId, Context.ConnectionId);

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(MatchmakingDelayMs);
                    _logger.LogInformation("[GameHub] Starting match for deviceId={DeviceId}", deviceId);
                    await _gameLoopService.StartMatchAsync(
                        team0DeviceIds: new List<string> { deviceId },
                        team1DeviceIds: new List<string>());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GameHub] StartMatchAsync failed for deviceId={DeviceId}", deviceId);
                }
            });
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called by the Unity client every tick to send its InputPacket.
        /// Steps:
        ///   1. Validate that the caller's ConnectionId maps to a known device/match.
        ///   2. Deserialise the packet.
        ///   3. Invoke the handler registered via INetworkService.OnInputReceived()
        ///      which routes the packet to IMatchManager.RouteInputToMatch().
        /// </summary>
        public Task SendInput(Guid matchId, InputPacket input)
        {
            var knownMatch = _networkService.GetMatchIdForConnection(Context.ConnectionId);
            if (knownMatch == null || knownMatch != matchId)
            {
                _logger.LogWarning("[GameHub] SendInput DROPPED — connId={ConnId} knownMatch={Known} requestedMatch={Req}",
                    Context.ConnectionId, knownMatch, matchId);
                return Task.CompletedTask;
            }

            _matchManager.RouteInputToMatch(matchId, input);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called automatically by SignalR when a Unity client connects.
        /// Does NOT add the client to a match group here — that happens in
        /// SignalRNetworkService.NotifyMatchStartedAsync() once matchmaking is complete.
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var deviceId = Context.GetHttpContext()?.Request.Query["deviceId"].ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(deviceId))
                _networkService.RegisterGameHubConnection(deviceId, Context.ConnectionId);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called automatically by SignalR when a Unity client disconnects.
        /// Steps:
        ///   1. Look up which match (if any) this ConnectionId belongs to.
        ///   2. Notify the Match that the player has disconnected.
        ///   3. If the match has no remaining connected players, trigger DestroyMatchAsync().
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var matchId = _networkService.GetMatchIdForConnection(Context.ConnectionId);
            _networkService.UnregisterGameHubConnection(Context.ConnectionId);

            if (matchId.HasValue)
            {
                var remaining = _networkService.RemoveConnectionFromMatch(Context.ConnectionId);
                _logger.LogInformation("[GameHub] Player disconnected from match {MatchId} — {Remaining} player(s) remaining",
                    matchId.Value, remaining);

                if (remaining == 0)
                {
                    _logger.LogInformation("[GameHub] No players left in match {MatchId}, destroying", matchId.Value);
                    await _matchManager.DestroyMatchAsync(matchId.Value);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
