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
        var user = await _userRepository.GetByDeviceIdAsync(deviceId);

        if (user != null)
        {
            return user;
        }

        user = new User
        {
            DeviceId = deviceId,
            UserName = "Guest_ " + Guid.NewGuid().ToString().Substring(0, 6)
        };

        await _userRepository.AddAsync(user);
        await _userRepository.SaveAsync();

        return user;    
    }
}
