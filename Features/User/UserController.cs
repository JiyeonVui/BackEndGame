using Microsoft.AspNetCore.Mvc;
using BackEndGame.Domain.Entities;

public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpPost("login")]
    public async Task<ActionResult<User>> Login([FromBody] string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return BadRequest("DeviceId is required");
        }

        var user = await _userService.LoginAsync(deviceId);
        return Ok(user);
    }
}

