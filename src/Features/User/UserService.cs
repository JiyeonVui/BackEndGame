using BackEndGame.Domain.Entities;
using BackEndGame.Infrastructure.Repositories;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User> LoginAsync(string deviceId, string? userName = null)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        var normalizedUserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();

        var user = await _userRepository.GetByDeviceIdAsync(normalizedDeviceId);

        if (user != null)
        {
            if (normalizedUserName != null && user.UserName != normalizedUserName)
            {
                user.UserName = normalizedUserName;
                await _userRepository.SaveAsync();
            }
            return user;
        }

        user = new User
        {
            DeviceId = normalizedDeviceId,
            UserName = normalizedUserName ?? "Guest_" + Guid.NewGuid().ToString("N")[..6]
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveAsync();

        return user;
    }

    public async Task<User?> FindByDeviceIdAsync(string deviceId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        return await _userRepository.GetByDeviceIdAsync(normalizedDeviceId);
    }

    private static string NormalizeDeviceId(string deviceId)
    {
        // Normalize input so "abc" and " abc " do not create different users.
        return deviceId.Trim();
    }
}
