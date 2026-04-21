using BackEndGame.Domain.Packets;
using BackEndGame.Game;

namespace BackEndGame.Features.Network
{
    /// <summary>
    /// Abstracts packet I/O between the server and Unity clients.
    /// The implementation can be SignalR WebSocket or raw UDP — callers don't care.
    /// </summary>
    public interface INetworkService
    {
        /// <summary>
        /// Broadcasts a GameStatePacket to every client in the specified match.
        /// Called once per tick by MatchManager.RunMatchLoopAsync() after Tick() returns.
        /// </summary>
        Task BroadcastGameStateAsync(Guid matchId, GameStatePacket packet);

        /// <summary>
        /// Sends a GameStatePacket to a single specific player (e.g., late-join sync).
        /// </summary>
        Task SendGameStateToPlayerAsync(string playerDeviceId, GameStatePacket packet);

        /// <summary>
        /// Registers a callback that fires every time a client sends an InputPacket.
        /// NetworkService calls this callback which then routes the packet to the
        /// correct match via IMatchManager.RouteInputToMatch().
        /// </summary>
        void OnInputReceived(Action<Guid, InputPacket> handler);

        /// <summary>
        /// Signal 1 of 2 — triggers Home → Loading screen transition on the client.
        /// Sent immediately after matchmaking confirms a match, before the game loop begins.
        /// Payload carries MatchId and each player's TeamId so the loading screen can
        /// display team assignment while assets load on the client side.
        /// </summary>
        Task NotifyMatchFoundAsync(Guid matchId, List<string> team0DeviceIds, List<string> team1DeviceIds);

        /// <summary>
        /// Signal 2 of 2 — triggers Loading → Game screen transition on the client.
        /// Sent after all players are added to the match SignalR group and the 64Hz loop is live.
        /// </summary>
        Task NotifyMatchStartedAsync(Guid matchId, IEnumerable<string> playerDeviceIds);

        /// <summary>
        /// Sends a match-end notification to all players in a match with the final result
        /// (win/lose/timeout) so the Unity client can show the end screen.
        /// </summary>
        Task NotifyMatchEndedAsync(Guid matchId, MatchResult result);
    }
}
