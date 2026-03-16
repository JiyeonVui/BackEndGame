using Microsoft.EntityFrameworkCore;
using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(User user)
        {
            await _context.Users.AddAsync(user);
        }

        public async Task<User?> GetByDeviceIdAsync(string deviceId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.DeviceId == deviceId);
        }

        public Task SaveAsync()
        {
            throw new NotImplementedException();
        }
    }
}
