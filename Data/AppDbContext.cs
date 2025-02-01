using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VCS_DOCs
{
	public class ApplicationDbContext : IdentityDbContext<IdentityUser>
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
		{
		}

		public DbSet<User> Users { get; set; }

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}


	// Модель для таблицы users
	public class User
	{
		public int Id { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string? Speciality { get; set; }
		public int StatusOnline { get; set; }
		public string? HardwareId { get; set; }
		public DateTime? LastEntry { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime UpdatedAt { get; set; }
		public int Access { get; set; }
	}
}
