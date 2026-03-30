using Microsoft.EntityFrameworkCore;
using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<FriendRequest> FriendRequests { get; set; }
        public DbSet<Friendship> Friendships { get; set; }
    
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // One device should map to one user in this prototype.
            modelBuilder.Entity<User>().HasIndex(u => u.DeviceId).IsUnique();

            // The pair is stored in sorted order so one friendship only has one row.
            modelBuilder.Entity<Friendship>()
                .HasIndex(f => new { f.UserOneId, f.UserTwoId })
                .IsUnique();

            modelBuilder.Entity<FriendRequest>()
                .HasIndex(r => new { r.ReceiverUserId, r.Status });
        }
    }
}
