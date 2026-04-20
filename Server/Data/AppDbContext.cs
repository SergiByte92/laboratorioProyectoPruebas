using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Server.Infrastructure;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<UserGroup> UsersGroups { get; set; }

        private readonly string connectionString;

        public AppDbContext(string _connectionString)
        {
            connectionString = _connectionString;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder
                .UseNpgsql(connectionString)
                .EnableDetailedErrors()
                .LogTo(
                    message =>
                    {
                        if (!string.IsNullOrWhiteSpace(message))
                            AppLogger.Debug("EF", message.Trim());
                    },
                    new[] { DbLoggerCategory.Database.Command.Name },
                    Microsoft.Extensions.Logging.LogLevel.Information);
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .Property(u => u.created_at)
                .HasDefaultValueSql("NOW()");

            modelBuilder.Entity<Group>()
                .Property(g => g.created_at)
                .HasDefaultValueSql("NOW()");
        }

        [Table("users")]
        public class User
        {
            public int id { get; set; }
            public string username { get; set; }
            public string email { get; set; }
            public string password { get; set; }
            public DateOnly birth_date { get; set; }
            public DateTime created_at { get; set; }

            public List<UserGroup> UserGroups { get; set; } = new();
        }

        [Table("group")]
        public class Group
        {
            public int id { get; set; }
            public string code { get; set; }
            public string name { get; set; }
            public string label { get; set; }
            public string description { get; set; }
            public string method { get; set; }
            public double? longitud { get; set; }
            public double? latitud { get; set; }
            public string? city { get; set; }
            public int userId { get; set; }
            public User user { get; set; }

            public bool isActive { get; set; }
            public DateTime created_at { get; set; }

            public List<UserGroup> UserGroups { get; set; } = new();
        }

        [Table("user_group")]
        [PrimaryKey(nameof(userId), nameof(groupId))]
        public class UserGroup
        {
            public int userId { get; set; }
            public User user { get; set; }
            public int groupId { get; set; }
            public Group group { get; set; }
        }
    }
}