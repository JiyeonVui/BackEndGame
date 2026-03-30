using Microsoft.EntityFrameworkCore;
using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure.Repositories
{
    public class FriendRequestRepository : IFriendRequestRepository
    {
        private readonly AppDbContext _context;

        public FriendRequestRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(FriendRequest friendRequest)
        {
            await _context.FriendRequests.AddAsync(friendRequest);
        }

        public async Task<FriendRequest?> GetByIdAsync(int requestId)
        {
            return await _context.FriendRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        }

        public async Task<bool> HasPendingRequestAsync(int senderUserId, int receiverUserId)
        {
            return await _context.FriendRequests.AnyAsync(r =>
                r.SenderUserId == senderUserId &&
                r.ReceiverUserId == receiverUserId &&
                r.Status == FriendRequestStatus.Pending);
        }

        public async Task<List<FriendRequest>> GetPendingIncomingAsync(int receiverUserId)
        {
            return await _context.FriendRequests
                .Where(r => r.ReceiverUserId == receiverUserId && r.Status == FriendRequestStatus.Pending)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
