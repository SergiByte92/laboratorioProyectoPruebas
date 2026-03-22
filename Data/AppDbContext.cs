using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<users> User {  get; set; }
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

        [Table("Users")]
        public class users 
        {
            public int id { get; set; } 
            public string username {  get; set; }
            public string email { get; set; }
            public string password { get; set; } //? o int?
            public DateOnly birth_date { get; set; }
            public DateTime created_at { get; set; }
        }

    }
}
