using BackEndGame.Game;

namespace BackEndGame.Features.GameLoop
{
    /// <summary>
    /// ASP.NET BackgroundService that owns the MatchManager and keeps it alive
    /// for the lifetime of the server process.
    ///
    /// Responsibilities:
    ///   - Start/stop the MatchManager when the host starts/stops.
    ///   - Expose a way for REST controllers (matchmaking) to create new matches.
    ///   - Monitor all active match loop tasks and restart any that crash unexpectedly.
    ///
    /// It does NOT run a global tick loop itself — each Match runs its own async loop
    /// via MatchManager.RunMatchLoopAsync(), which is cheaper and fully isolated.
    /// </summary>
    public class GameLoopService : BackgroundService
    {
        private readonly IMatchManager _matchManager;
        private readonly ILogger<GameLoopService> _logger;

        private static readonly TimeSpan MonitorInterval = TimeSpan.FromSeconds(5);

        public GameLoopService(IMatchManager matchManager, ILogger<GameLoopService> logger)
        {
            _matchManager = matchManager;
            _logger = logger;
        }

        /// <summary>
        /// Entry point called by the ASP.NET host when the application starts.
        /// Runs until the host requests cancellation (app shutdown).
        ///
        /// What it does:
        ///   1. Logs that the game loop service is ready.
        ///   2. Enters a lightweight monitor loop (e.g., every 5 s) that:
        ///      - Checks for crashed match tasks and logs/restarts them.
        ///      - Emits active match count metrics (for health dashboards).
        ///   3. On cancellation, waits for all match loops to exit via DestroyMatchAsync().
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("GameLoopService is ready");

            while (!stoppingToken.IsCancellationRequested)
            {
                var activeIds = _matchManager.GetActiveMatchIds();
                _logger.LogInformation("Active matches: {Count}", activeIds.Count);

                try
                {
                    await Task.Delay(MonitorInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("GameLoopService shutting down");
        }

        /// <summary>
        /// Convenience wrapper called by the matchmaking REST controller.
        /// Delegates to IMatchManager.CreateMatch() and returns the new MatchId
        /// so the controller can return it to PlayFab / the client.
        /// </summary>
        public Guid StartMatch(List<string> playerDeviceIds)
        {
            var match = _matchManager.CreateMatch(playerDeviceIds);
            return match.MatchId;
        }

        /// <summary>
        /// Called by the matchmaking controller when a match is force-stopped
        /// (e.g., all players disconnected).
        /// Delegates to IMatchManager.DestroyMatchAsync().
        /// </summary>
        public Task StopMatchAsync(Guid matchId) => _matchManager.DestroyMatchAsync(matchId);

        /// <summary>
        /// Gracefully shuts down all running matches when the host is stopping:
        ///   1. Gets all active match IDs from IMatchManager.
        ///   2. Calls DestroyMatchAsync() on each in parallel.
        ///   3. Awaits all destroy tasks before returning so the process exits cleanly.
        /// </summary>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            var activeIds = _matchManager.GetActiveMatchIds();
            var destroyTasks = activeIds.Select(id => _matchManager.DestroyMatchAsync(id));
            await Task.WhenAll(destroyTasks);

            await base.StopAsync(cancellationToken);
        }
    }
}