using Microsoft.AspNetCore.Mvc;
using BackEndGame.Domain.Entities;

[ApiController]
[Route("api/users")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<User>> Login([FromBody] LoginRequest request)
    {
        // A DTO makes the API contract clearer than accepting a raw JSON string.
        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            return BadRequest("DeviceId is required");
        }

        var user = await _userService.LoginAsync(request.DeviceId, request.UserName);
        return Ok(user);
    }

    [HttpGet("search")]
    public async Task<ActionResult<UserLookupResponse>> SearchByDeviceId([FromQuery] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("DeviceId is required");
        }

        var user = await _userService.FindByDeviceIdAsync(deviceId);
        if (user == null)
        {
            return NotFound("User not found");
        }

        return Ok(new UserLookupResponse
        {
            Id = user.Id,
            DeviceId = user.DeviceId,
            UserName = user.UserName
        });
    }
}
