using BackEndGame.Domain.Entities;
using BackEndGame.Infrastructure.Repositories;

public class FriendService : IFriendService
{
    private readonly IUserRepository _userRepository;
    private readonly IFriendRequestRepository _friendRequestRepository;
    private readonly IFriendshipRepository _friendshipRepository;

    public FriendService(
        IUserRepository userRepository,
        IFriendRequestRepository friendRequestRepository,
        IFriendshipRepository friendshipRepository)
    {
        _userRepository = userRepository;
        _friendRequestRepository = friendRequestRepository;
        _friendshipRepository = friendshipRepository;
    }

    public async Task<FriendRequestActionResponse> SendFriendRequestAsync(SendFriendRequestRequest request)
    {
        var sender = await GetUserByDeviceIdOrThrowAsync(request.SenderDeviceId, "Sender");
        var receiver = await GetUserByDeviceIdOrThrowAsync(request.ReceiverDeviceId, "Receiver");

        if (sender.Id == receiver.Id)
        {
            throw new InvalidOperationException("You cannot send a friend request to yourself.");
        }

        var (userOneId, userTwoId) = NormalizeFriendshipPair(sender.Id, receiver.Id);
        if (await _friendshipRepository.ExistsAsync(userOneId, userTwoId))
        {
            throw new InvalidOperationException("These users are already friends.");
        }

        // Prevent duplicate pending requests in either direction.
        if (await _friendRequestRepository.HasPendingRequestAsync(sender.Id, receiver.Id) ||
            await _friendRequestRepository.HasPendingRequestAsync(receiver.Id, sender.Id))
        {
            throw new InvalidOperationException("A pending friend request already exists between these users.");
        }

        var friendRequest = new FriendRequest
        {
            SenderUserId = sender.Id,
            ReceiverUserId = receiver.Id
        };

        await _friendRequestRepository.AddAsync(friendRequest);
        await _friendRequestRepository.SaveAsync();

        return new FriendRequestActionResponse
        {
            RequestId = friendRequest.Id,
            Status = friendRequest.Status.ToString(),
            Message = $"Friend request sent to {receiver.UserName}."
        };
    }

    public async Task<List<FriendRequestNotificationResponse>> GetIncomingNotificationsAsync(string deviceId)
    {
        var receiver = await GetUserByDeviceIdOrThrowAsync(deviceId, "User");
        var pendingRequests = await _friendRequestRepository.GetPendingIncomingAsync(receiver.Id);
        var notifications = new List<FriendRequestNotificationResponse>();

        foreach (var pendingRequest in pendingRequests)
        {
            var sender = await _userRepository.GetByIdAsync(pendingRequest.SenderUserId);
            if (sender == null)
            {
                continue;
            }

            // The front-end can poll this endpoint and render each pending request as a notification.
            notifications.Add(new FriendRequestNotificationResponse
            {
                RequestId = pendingRequest.Id,
                SenderUserId = sender.Id,
                SenderDeviceId = sender.DeviceId,
                SenderUserName = sender.UserName,
                CreatedAt = pendingRequest.CreatedAt,
                Message = $"{sender.UserName} sent you a friend request."
            });
        }

        return notifications;
    }

    public async Task<FriendRequestActionResponse> RespondToRequestAsync(int requestId, RespondToFriendRequestRequest request)
    {
        var receiver = await GetUserByDeviceIdOrThrowAsync(request.ReceiverDeviceId, "Receiver");
        var friendRequest = await _friendRequestRepository.GetByIdAsync(requestId);

        if (friendRequest == null)
        {
            throw new KeyNotFoundException("Friend request not found.");
        }

        if (friendRequest.ReceiverUserId != receiver.Id)
        {
            throw new InvalidOperationException("Only the receiver can respond to this friend request.");
        }

        if (friendRequest.Status != FriendRequestStatus.Pending)
        {
            throw new InvalidOperationException("This friend request has already been handled.");
        }

        friendRequest.Status = request.Accept
            ? FriendRequestStatus.Accepted
            : FriendRequestStatus.Rejected;
        friendRequest.RespondedAt = DateTime.UtcNow;

        if (request.Accept)
        {
            var (userOneId, userTwoId) = NormalizeFriendshipPair(friendRequest.SenderUserId, friendRequest.ReceiverUserId);

            if (!await _friendshipRepository.ExistsAsync(userOneId, userTwoId))
            {
                await _friendshipRepository.AddAsync(new Friendship
                {
                    UserOneId = userOneId,
                    UserTwoId = userTwoId
                });

                await _friendshipRepository.SaveAsync();
            }
        }

        await _friendRequestRepository.SaveAsync();

        return new FriendRequestActionResponse
        {
            RequestId = friendRequest.Id,
            Status = friendRequest.Status.ToString(),
            Message = request.Accept
                ? "Friend request accepted."
                : "Friend request rejected."
        };
    }

    public async Task<List<FriendSummaryResponse>> GetFriendsAsync(string deviceId)
    {
        var user = await GetUserByDeviceIdOrThrowAsync(deviceId, "User");
        var friendships = await _friendshipRepository.GetByUserIdAsync(user.Id);
        var friends = new List<FriendSummaryResponse>();

        foreach (var friendship in friendships)
        {
            var friendUserId = friendship.UserOneId == user.Id
                ? friendship.UserTwoId
                : friendship.UserOneId;

            var friend = await _userRepository.GetByIdAsync(friendUserId);
            if (friend == null)
            {
                continue;
            }

            friends.Add(new FriendSummaryResponse
            {
                UserId = friend.Id,
                DeviceId = friend.DeviceId,
                UserName = friend.UserName,
                BecameFriendsAt = friendship.CreatedAt
            });
        }

        return friends;
    }

    private async Task<User> GetUserByDeviceIdOrThrowAsync(string deviceId, string role)
    {
        var normalizedDeviceId = NormalizeDeviceId(deviceId);

        if (string.IsNullOrWhiteSpace(normalizedDeviceId))
        {
            throw new InvalidOperationException($"{role} DeviceId is required.");
        }

        var user = await _userRepository.GetByDeviceIdAsync(normalizedDeviceId);
        if (user == null)
        {
            throw new KeyNotFoundException($"{role} user not found.");
        }

        return user;
    }

    private static string NormalizeDeviceId(string deviceId)
    {
        return deviceId.Trim();
    }

    private static (int userOneId, int userTwoId) NormalizeFriendshipPair(int firstUserId, int secondUserId)
    {
        // Sorting the pair lets us store one friendship row for both directions.
        return firstUserId < secondUserId
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
    }
}
