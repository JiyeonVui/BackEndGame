public class LoginRequest
{
    // Keep the request payload small and explicit for the login API.
    public string DeviceId { get; set; } = string.Empty;
}
