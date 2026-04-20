using BackEndGame.Domain.Packets;
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

        public GameHub(IMatchManager matchManager, SignalRNetworkService networkService)
        {
            _matchManager = matchManager;
            _networkService = networkService;
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
                return Task.CompletedTask; // Connection not registered to this match — drop packet

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
                var match = _matchManager.GetMatch(matchId.Value);
                if (match != null)
                {
                    // No remaining connected players in this match — destroy it
                    var remainingConnected = _networkService.GetMatchIdForConnection(Context.ConnectionId);
                    if (remainingConnected == null)
                        await _matchManager.DestroyMatchAsync(matchId.Value);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
