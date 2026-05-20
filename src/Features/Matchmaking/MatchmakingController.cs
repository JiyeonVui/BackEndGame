using Microsoft.AspNetCore.Mvc;
using BackEndGame.Features.GameLoop;

namespace BackEndGame.Features.Matchmaking
{
    [ApiController]
    [Route("api/matchmaking")]
    public class MatchmakingController : ControllerBase
    {
        private readonly GameLoopService _gameLoopService;
        private readonly ILogger<MatchmakingController> _logger;

        private const int MatchmakingDelayMs = 5000;

        public MatchmakingController(GameLoopService gameLoopService, ILogger<MatchmakingController> logger)
        {
            _gameLoopService = gameLoopService;
            _logger = logger;
        }

        /// <summary>
        /// Simulates matchmaking: waits 5s then creates a solo match for the requesting player.
        /// Used for client-server flow testing.
        /// </summary>
        [HttpPost("find")]
        public async Task<IActionResult> FindMatch([FromBody] FindMatchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.DeviceId))
                return BadRequest("DeviceId is required");

            _logger.LogInformation("FindMatch requested by {DeviceId}, waiting {Delay}ms...",
                request.DeviceId, MatchmakingDelayMs);

            await Task.Delay(MatchmakingDelayMs);

            var matchId = await _gameLoopService.StartMatchAsync(
                team0DeviceIds: new List<string> { request.DeviceId },
                team1DeviceIds: new List<string>()
            );

            _logger.LogInformation("Match {MatchId} created for {DeviceId}", matchId, request.DeviceId);

            return Ok(new { matchId });
        }
    }
}