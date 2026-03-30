public class FriendSummaryResponse
{
    public int UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime BecameFriendsAt { get; set; }
}
