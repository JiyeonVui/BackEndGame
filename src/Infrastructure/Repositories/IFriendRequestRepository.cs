using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure.Repositories
{
    public interface IFriendRequestRepository
    {
        Task AddAsync(FriendRequest friendRequest);
        Task<FriendRequest?> GetByIdAsync(int requestId);
        Task<bool> HasPendingRequestAsync(int senderUserId, int receiverUserId);
        Task<List<FriendRequest>> GetPendingIncomingAsync(int receiverUserId);
        Task SaveAsync();
    }
}
