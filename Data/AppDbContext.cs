using System;
using System.ComponentModel.DataAnnotations;
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

		public DbSet<FileUploadSession> FileUploadSessions { get; set; }
		public DbSet<FileUploadChunk> FileUploadChunks { get; set; } = null!;

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}

	public class User : Microsoft.AspNetCore.Identity.IdentityUser
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
		public bool IsDeleted { get; set; } = false;
	}

	public class FileUploadSession
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public Guid FileId { get; set; } = Guid.NewGuid(); // Уникальный идентификатор файла

		public string UserId { get; set; } = null!;
		public string OriginalFileName { get; set; } = null!;
		public string FileHash { get; set; } = null!;

		public long FileSize { get; set; }
		public int TotalChunks { get; set; }
		public string Status { get; set; } = "pending"; // pending, complete, failed

		public DateTime CreatedAt { get; set; } = DateTime.Now;
		public DateTime UpdatedAt { get; set; } = DateTime.Now;

		public List<FileUploadChunk> Chunks { get; set; } = [];

		public int Version { get; set; } = 1;
		public bool IsLatest { get; set; } = false;
	}

	public class FileUploadChunk
	{
		[Key]
		public Guid Id { get; set; } = Guid.NewGuid();

		public Guid SessionId { get; set; }
		public FileUploadSession Session { get; set; } = null!;

		public int Index { get; set; }
		public bool Uploaded { get; set; }
		public DateTime UpdatedAt { get; set; } = DateTime.Now;
	}
}
