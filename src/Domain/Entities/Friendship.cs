namespace BackEndGame.Domain.Entities
{
    public class Friendship
    {
        public int Id { get; set; }
        public int UserOneId { get; set; }
        public int UserTwoId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
