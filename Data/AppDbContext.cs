using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace VCS_DOCs
{
	// Контекст приложения
	public class ApplicationDbContext : IdentityDbContext<User>
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options)
		{
		}

		// Таблица для хранения резервации места под файлы
		public DbSet<FileReservation> FileReservations { get; set; }

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}

	// Расширенная сущность пользователя
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

	// Сущность для хранения резервации байт под файлы
	public class FileReservation
	{
		public int Id { get; set; }          // PK
		public string UserId { get; set; } = null!; // FK → AspNetUsers(Id)
		public User User { get; set; } = null!;
		public string FileName { get; set; } = null!;
		public long ReservedBytes { get; set; }          // Сколько байт зарезервировано
		public bool IsReleased { get; set; }          // Флаг снятия резерва
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	}
}
