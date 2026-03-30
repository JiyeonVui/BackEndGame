using BackEndGame.Domain.Entities;
using BackEndGame.Infrastructure.Repositories;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<User> LoginAsync(string deviceId)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);
        var user = await _userRepository.GetByDeviceIdAsync(normalizedDeviceId);

        if (user != null)
        {
            return user;
        }

        // The first login from a device creates a guest account.
        user = new User
        {
            DeviceId = normalizedDeviceId,
            UserName = "Guest_" + Guid.NewGuid().ToString("N")[..6]
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
