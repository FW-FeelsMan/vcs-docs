using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VCS_DOCs
{
	public class ApplicationDbContext : IdentityDbContext<User>
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options)
		{
		}

		public DbSet<FileReservation> FileReservations { get; set; }
		public DbSet<ChunkStatus> ChunkStatuses { get; set; }

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}

	public class User : IdentityUser
	{
		public string FullName { get; set; } = "Не установлено";
		public string DateOfBirth { get; set; } = "Не установлено";
		public string Organization { get; set; } = "Не установлено";
		public string Department { get; set; } = "Не установлено";
		public string? Speciality { get; set; }
		public int StatusOnline { get; set; }
		public string? HardwareId { get; set; }
		public DateTime? LastEntry { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime UpdatedAt { get; set; }
		public int Access { get; set; }
		public string? JwtId { get; set; }
	}

	public class FileReservation
	{
		public int Id { get; set; }
		public string UserId { get; set; } = null!;
		public User User { get; set; } = null!;
		public string FileName { get; set; } = null!;
		public long ReservedBytes { get; set; }
		public bool IsReleased { get; set; }
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}

	public class ChunkStatus
	{
		public int Id { get; set; }
		public string UserId { get; set; } = null!;
		public User User { get; set; } = null!;
		public string ChunkFolder { get; set; } = null!;
		public long TotalBytes { get; set; }
		public bool IsActive { get; set; }
		public DateTime UpdatedAt { get; set; }
	}
}
