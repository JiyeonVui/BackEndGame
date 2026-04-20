using BackEndGame.Domain.Packets;

namespace BackEndGame.Game
{
    /// <summary>
    /// Manages the lifecycle of all active matches on the server.
    /// Consumed by GameLoopService (tick routing) and REST controllers (matchmaking).
    /// </summary>
    public interface IMatchManager
    {
        /// <summary>
        /// Creates a new isolated Match for the given players, loads the map, and
        /// starts the match loop task. Returns the created Match instance.
        /// </summary>
        Match CreateMatch(List<string> playerDeviceIds);

        /// <summary>
        /// Cancels the match loop, disposes physics/navmesh resources, reports final
        /// stats to PlayFab, and removes the match from the active dictionary.
        /// </summary>
        Task DestroyMatchAsync(Guid matchId);

        /// <summary>
        /// Returns the Match for the given ID, or null if not found.
        /// Used by NetworkService to look up which match an incoming packet belongs to.
        /// </summary>
        Match? GetMatch(Guid matchId);

        /// <summary>
        /// Forwards an InputPacket to the correct match's input queue.
        /// Called by NetworkService on every packet received from a client.
        /// </summary>
        void RouteInputToMatch(Guid matchId, InputPacket input);

        /// <summary>Returns a read-only snapshot of all currently active match IDs.</summary>
        IReadOnlyCollection<Guid> GetActiveMatchIds();
    }
}
