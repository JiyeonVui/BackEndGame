using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/realtime")]
public class RealtimeController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IRealtimeConnectionTracker _connectionTracker;

    public RealtimeController(IUserService userService, IRealtimeConnectionTracker connectionTracker)
    {
        _userService = userService;
        _connectionTracker = connectionTracker;
    }

    [HttpGet("presence")]
    public async Task<ActionResult<ConnectionStatusResponse>> GetPresence([FromQuery] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("DeviceId is required.");
        }

        var user = await _userService.FindByDeviceIdAsync(deviceId);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        return Ok(new ConnectionStatusResponse
        {
            DeviceId = user.DeviceId,
            IsOnline = _connectionTracker.IsOnline(user.DeviceId),
            ActiveConnections = _connectionTracker.GetConnectionCount(user.DeviceId)
        });
    }
}
