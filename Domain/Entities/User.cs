namespace BackEndGame.Domain.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string DeviceId { get; set; }
        public string UserName { get; set; }
        public DateTime CreateAt { get; set; } = DateTime.UtcNow;
    }
}
