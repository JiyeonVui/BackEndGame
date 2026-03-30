using Microsoft.EntityFrameworkCore;
using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure.Repositories
{
    public class FriendshipRepository : IFriendshipRepository
    {
        private readonly AppDbContext _context;

        public FriendshipRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Friendship friendship)
        {
            await _context.Friendships.AddAsync(friendship);
        }

        public async Task<bool> ExistsAsync(int userOneId, int userTwoId)
        {
            return await _context.Friendships.AnyAsync(f =>
                f.UserOneId == userOneId &&
                f.UserTwoId == userTwoId);
        }

        public async Task<List<Friendship>> GetByUserIdAsync(int userId)
        {
            return await _context.Friendships
                .Where(f => f.UserOneId == userId || f.UserTwoId == userId)
                .OrderByDescending(f => f.CreatedAt)
                .ToListAsync();
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
