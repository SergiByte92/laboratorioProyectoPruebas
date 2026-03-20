using Microsoft.EntityFrameworkCore;

namespace Data
{
    public class AppDbContext : DbContext
    {
        

        public class user 
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Password { get; set; } //? o int?
            public string Email { get; set; }
        }

    }
}
