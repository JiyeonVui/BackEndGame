using BackEndGame.Domain.Entities;

public interface IUserService
{
    Task<User> LoginAsync(string deviceId);
    Task<User?> FindByDeviceIdAsync(string deviceId);
}
