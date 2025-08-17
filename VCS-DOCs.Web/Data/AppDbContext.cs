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

        public DbSet<FileUploadSessionModel> FileUploadSessions { get; set; } = default!;
        public DbSet<ServerSettingModel> ServerSettings { get; set; } = default!;

        public DbSet<SharedLink> SharedLinks { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<SharedLink>(e =>
            {
                e.ToTable("SharedLinks");
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.FileGroupId, x.Version });

                e.Property(x => x.FileGroupId).HasConversion(
                    v => v.ToString("D"),
                    v => Guid.Parse(v)
                );
                e.Property(x => x.RequireAuth).HasConversion<int>(); 
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
        }
    }
}
