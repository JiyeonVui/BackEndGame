using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure.Repositories
{
    public interface IFriendshipRepository
    {
        Task AddAsync(Friendship friendship);
        Task<bool> ExistsAsync(int userOneId, int userTwoId);
        Task<List<Friendship>> GetByUserIdAsync(int userId);
        Task SaveAsync();
    }
}
