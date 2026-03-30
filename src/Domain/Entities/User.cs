namespace BackEndGame.Domain.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string DeviceId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;

        // Set the creation timestamp when a new entity is instantiated.
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
