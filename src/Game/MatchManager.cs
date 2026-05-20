using System.Collections.Concurrent;
using BackEndGame.Domain.Packets;
using BackEndGame.Features.Network;
using Microsoft.Extensions.Logging;

namespace BackEndGame.Game
{
    /// <summary>
    /// Singleton that owns all active Match instances.
    /// Thread-safe: multiple NetworkService threads may call RouteInputToMatch concurrently.
    /// </summary>
    public class MatchManager : IMatchManager
    {
        // Keyed by MatchId. ConcurrentDictionary used for thread-safe reads from NetworkService.
        private readonly ConcurrentDictionary<Guid, Match> _activeMatches = new();

        // Holds the CancellationTokenSource for each match loop task, keyed by MatchId.
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _matchCts = new();

        // Holds the running loop task for each match so DestroyMatchAsync can await clean exit.
        private readonly ConcurrentDictionary<Guid, Task> _matchTasks = new();

        private readonly INetworkService _networkService;
        private readonly ILogger<MatchManager> _logger;

        public MatchManager(INetworkService networkService, ILogger<MatchManager> logger)
        {
            _networkService = networkService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a new Match, calls Match.Start() to initialise physics and map,
        /// then fires off a background Task running the 64Hz game loop.
        /// The loop task is stored (not awaited) — it runs until the CancellationToken is cancelled.
        /// </summary>
        public Match CreateMatch(List<string> team0DeviceIds, List<string> team1DeviceIds)
        {
            var match = new Match(Guid.NewGuid(), team0DeviceIds, team1DeviceIds);
            match.Start();

            var cts = new CancellationTokenSource();
            _matchCts[match.MatchId] = cts;
            _activeMatches[match.MatchId] = match;

            var loopTask = Task.Run(() => RunMatchLoopAsync(match, cts.Token), cts.Token);
            _matchTasks[match.MatchId] = loopTask;

            _logger.LogInformation("Match {MatchId} created — Team0: {T0}, Team1: {T1}",
                match.MatchId, team0DeviceIds.Count, team1DeviceIds.Count);
            return match;
        }

        /// <summary>
        /// Cancels the loop task by signalling the match's CancellationTokenSource,
        /// waits for the loop to exit cleanly, calls Match.Stop() to free BEPU + navmesh memory,
        /// reports final kill/death stats to PlayFab via the Azure SDK,
        /// then removes the match from _activeMatches.
        /// </summary>
        public async Task DestroyMatchAsync(Guid matchId)
        {
            if (!_matchCts.TryRemove(matchId, out var cts))
                return; // Already destroyed or never existed

            cts.Cancel();

            if (_matchTasks.TryRemove(matchId, out var loopTask))
            {
                try
                {
                    await loopTask;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Match {MatchId} loop threw unexpectedly during shutdown", matchId);
                }
            }

            if (_activeMatches.TryRemove(matchId, out var match))
            {
                match.Stop();
                // TODO: Report final kill/death stats to PlayFab via Azure SDK
                _logger.LogInformation("Match {MatchId} destroyed. Result: {Result}", matchId, match.LastResult);
            }

            cts.Dispose();
        }

        /// <summary>
        /// Looks up the match by ID. Returns null without throwing if the match
        /// no longer exists (e.g., destroyed between route and lookup).
        /// </summary>
        public Match? GetMatch(Guid matchId)
        {
            _activeMatches.TryGetValue(matchId, out var match);
            return match;
        }

        /// <summary>
        /// Calls Match.EnqueueInput() on the target match.
        /// No-ops silently if the match is not found — avoids crashing on late packets
        /// arriving after a match has been destroyed.
        /// </summary>
        public void RouteInputToMatch(Guid matchId, InputPacket input)
        {
            if (_activeMatches.TryGetValue(matchId, out var match))
                match.EnqueueInput(input);
        }

        public IReadOnlyCollection<Guid> GetActiveMatchIds() => _activeMatches.Keys.ToList();

        // ─── Private ──────────────────────────────────────────────────────────────

        /// <summary>
        /// The actual 64Hz game loop for one match.
        /// Each iteration: calls match.Tick(), gets a GameStatePacket,
        /// broadcasts it to all players via INetworkService,
        /// then awaits Task.Delay(tickInterval) to yield the thread back to the pool.
        /// If Tick() signals a win/lose/timeout, calls DestroyMatchAsync() and exits.
        /// </summary>
        private async Task RunMatchLoopAsync(Match match, CancellationToken ct)
        {
            var tickInterval = TimeSpan.FromSeconds(1.0 / 64.0);

            while (!ct.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;

                var state = match.Tick();
                _logger.LogDebug("[MatchManager] Tick={Tick} Players={Count} — {States}",
                    state.Tick, state.Players.Length,
                    string.Join(" | ", state.Players.Select(p => $"P{p.PlayerId}({p.PosX:F1},{p.PosY:F1},{p.PosZ:F1}) hp={p.Hp}")));

                await _networkService.BroadcastGameStateAsync(match.MatchId, state);

                if (match.LastResult != MatchResult.Ongoing)
                {
                    await _networkService.NotifyMatchEndedAsync(match.MatchId, match.LastResult);
                    // Fire-and-forget: return first so the stored task completes, then DestroyMatchAsync awaits it.
                    _ = DestroyMatchAsync(match.MatchId);
                    return;
                }

                var elapsed = DateTime.UtcNow - tickStart;
                var wait = tickInterval - elapsed;
                if (wait > TimeSpan.Zero)
                {
                    try { await Task.Delay(wait, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }
}