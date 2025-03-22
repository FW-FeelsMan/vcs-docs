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

		public new DbSet<User> Users { get; set; }

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}

	public class User
	{
		public int Id { get; set; }
		public string Username { get; set; } = string.Empty;
		public string Password { get; set; } = string.Empty; 
		public string? Speciality { get; set; }
		public int StatusOnline { get; set; }
		public string? HardwareId { get; set; }
		public DateTime? LastEntry { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime UpdatedAt { get; set; }
		public int Access { get; set; }
		public string? JwtId { get; set; }
		public string FullName { get; set; } = "Не установлено";
		public string DateOfBirth { get; set; } = "Не установлено";
		public string Organization { get; set; } = "Не установлено";
		public string Department { get; set; } = "Не установлено";
	}
}
