using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users {  get; set; }
        public DbSet<Group> Groups {  get; set; }
        string connectionString;
        public AppDbContext(string _connectionString) 
        {
            connectionString = _connectionString;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.UseNpgsql(connectionString);
            optionsBuilder.LogTo(Console.WriteLine);
        }

        [Table("users")]
        public class User 
        {
            public int id { get; set; } 
            public string username {  get; set; }
            public string email { get; set; }
            public string password { get; set; } //? o int?
            public DateOnly birth_date { get; set; }
            public DateTime created_at { get; set; }
            
            // Relación con la intermedia
            public List<UserGroup> UserGroups { get; set; } = new();
        }
        [Table("group")]
        public class Group 
        {
            public int id { get; set;  }
            public Guid guid { get; set; }
            public string name { get; set; }
            public string description { get; set; }
            public double longitud { get; set; }
            public double latitud { get; set; }
            public string city { get; set; }

            // Relación con la intermedia
            public List<UserGroup> UserGroups { get; set; } = new();
        }
        [Table("UserGroup")]
        [PrimaryKey(nameof(userId),nameof(groupId))]
        public class UserGroup 
        {
            public int userId { get; set; }
            public User user { get; set; } // navegador
            public int groupId { get; set; }
            public Group group { get; set; } // navegador
        }

    }
}
