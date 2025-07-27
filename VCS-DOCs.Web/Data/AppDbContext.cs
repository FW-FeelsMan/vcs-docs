using VCS_DOCs.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;

namespace VCS_DOCs.Data
{
	public partial class ApplicationDbContext : IdentityDbContext<User>, IUploadDbContext
	{
		public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
			: base(options)
		{
		}
		public DbSet<FileUploadSessionModel> FileUploadSessions { get; set; }
		public DbSet<ServerSettingModel> ServerSettings { get; set; } 

		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (!optionsBuilder.IsConfigured)
			{
				optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
			}
		}
	}
}
