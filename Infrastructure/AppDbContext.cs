using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using BackEndGame.Domain.Entities;

namespace BackEndGame.Infrastructure
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
    
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Dam bao DeviceId la unique
            modelBuilder.Entity<User>().HasIndex(u => u.DeviceId).IsUnique();
        }
    }
}
