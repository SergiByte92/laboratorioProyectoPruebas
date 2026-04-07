using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Server.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<Group> Groups { get; set; }
        public DbSet<UserGroup> UsersGroups { get; set; }
        string connectionString;
        public AppDbContext(string _connectionString)
        {
            connectionString = _connectionString;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            optionsBuilder.UseNpgsql(connectionString);
            optionsBuilder.LogTo(Console.WriteLine); // funcion estatica log to file guardarla en ficheros
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
            public string password { get; set; } //? o int?
            public DateOnly birth_date { get; set; }
            public DateTime created_at { get; set; } // no sale??? o no va esta columna????

            // Relación con la intermedia
            public List<UserGroup> UserGroups { get; set; } = new();
        }
        [Table("group")]
        public class Group
        {
            public int id { get; set; }
            public string code { get; set; }
            public string name { get; set; }
            public string label { get; set; } // motivo de quedada
            public string description { get; set; } // descripción si quiere
            public string method { get; set; } //metodo para averiguar el punto de quedada
            public double? longitud { get; set; } // Estos datos hasta que no lo calculo no lo se, quizas otra tabla para esto
            public double? latitud { get; set; }
            public string? city { get; set; }
            public int userId { get; set; }   // FK
            public User user { get; set; }    // navegación

            public bool isActive { get; set; } // mientras se une la gente deberia estar activo, una vez que se pasa a calcular el punto optimo, deberia pasar a false
            public DateTime created_at { get; set; } // añadir detector de luz?

            // Relación con la intermedia
            public List<UserGroup> UserGroups { get; set; } = new();
        }
        [Table("user_group")]
        [PrimaryKey(nameof(userId), nameof(groupId))]
        public class UserGroup
        {
            public int userId { get; set; }
            public User user { get; set; } // navegador
            public int groupId { get; set; }
            public Group group { get; set; } // navegador
        }

    }
}
