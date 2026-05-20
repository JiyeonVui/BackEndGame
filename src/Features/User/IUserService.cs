using BackEndGame.Domain.Entities;

public interface IUserService
{
    Task<User> LoginAsync(string deviceId, string? userName = null);
    Task<User?> FindByDeviceIdAsync(string deviceId);
}
