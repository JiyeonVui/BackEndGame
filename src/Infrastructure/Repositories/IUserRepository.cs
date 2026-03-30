using BackEndGame.Domain.Entities;
namespace BackEndGame.Infrastructure.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByDeviceIdAsync(string deviceId);
        Task<User?> GetByIdAsync(int id);
        Task AddAsync(User user);
        Task SaveAsync();
    }
}
