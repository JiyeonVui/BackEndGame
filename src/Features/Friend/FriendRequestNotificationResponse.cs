public class FriendRequestNotificationResponse
{
    public int RequestId { get; set; }
    public int SenderUserId { get; set; }
    public string SenderDeviceId { get; set; } = string.Empty;
    public string SenderUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Message { get; set; } = string.Empty;
}
