public interface IFriendService
{
    Task<FriendRequestActionResponse> SendFriendRequestAsync(SendFriendRequestRequest request);
    Task<List<FriendRequestNotificationResponse>> GetIncomingNotificationsAsync(string deviceId);
    Task<FriendRequestActionResponse> RespondToRequestAsync(int requestId, RespondToFriendRequestRequest request);
    Task<List<FriendSummaryResponse>> GetFriendsAsync(string deviceId);
}
