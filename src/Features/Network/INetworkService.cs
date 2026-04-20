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
        /// Sends a match-start notification to all players in a match,
        /// including the MatchId they should use for subsequent InputPacket routing.
        /// </summary>
        Task NotifyMatchStartedAsync(Guid matchId, IEnumerable<string> playerDeviceIds);

        /// <summary>
        /// Sends a match-end notification to all players in a match with the final result
        /// (win/lose/timeout) so the Unity client can show the end screen.
        /// </summary>
        Task NotifyMatchEndedAsync(Guid matchId, MatchResult result);
    }
}
