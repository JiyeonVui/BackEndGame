using BackEndGame.Domain.Entities;
namespace BackEndGame.Infrastructure.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByDeviceIdAsync(string deviceId);
        Task AddAsync(User user);
        Task SaveAsync();
    }
}
