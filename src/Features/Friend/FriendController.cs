using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/friends")]
public class FriendController : ControllerBase
{
    private readonly IFriendService _friendService;

    public FriendController(IFriendService friendService)
    {
        _friendService = friendService;
    }

    [HttpPost("requests")]
    public async Task<ActionResult<FriendRequestActionResponse>> SendFriendRequest([FromBody] SendFriendRequestRequest request)
    {
        try
        {
            var response = await _friendService.SendFriendRequestAsync(request);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("requests/notifications")]
    public async Task<ActionResult<List<FriendRequestNotificationResponse>>> GetNotifications([FromQuery] string deviceId)
    {
        try
        {
            var notifications = await _friendService.GetIncomingNotificationsAsync(deviceId);
            return Ok(notifications);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("requests/{requestId:int}/respond")]
    public async Task<ActionResult<FriendRequestActionResponse>> RespondToRequest(int requestId, [FromBody] RespondToFriendRequestRequest request)
    {
        try
        {
            var response = await _friendService.RespondToRequestAsync(requestId, request);
            return Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<FriendSummaryResponse>>> GetFriends([FromQuery] string deviceId)
    {
        try
        {
            var friends = await _friendService.GetFriendsAsync(deviceId);
            return Ok(friends);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
