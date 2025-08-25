using VCS_DOCs.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;

namespace VCS_DOCs.Data
{
    public partial class ApplicationDbContext : IdentityDbContext<User>, IUploadDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<SupportUserSession> SupportUserSessions => Set<SupportUserSession>();
        public DbSet<SupportUserConnection> SupportUserConnections => Set<SupportUserConnection>();

        public DbSet<FileUploadSessionModel> FileUploadSessions { get; set; } = default!;
        public DbSet<ServerSettingModel> ServerSettings { get; set; } = default!;
        public DbSet<SharedLink> SharedLinks { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            b.Entity<SupportUserConnection>(e =>
            {
                e.ToTable("SupportUserConnections");
                e.HasKey(x => x.ConnectionId);
                e.Property(x => x.ConnectionId).HasMaxLength(128);
                e.Property(x => x.UserId).HasMaxLength(64).IsRequired();
                e.HasIndex(x => x.UserId);
            });

            b.Entity<SupportUserSession>(e =>
            {
                e.ToTable("SupportUserSessions");
                e.HasKey(x => x.UserId);
                e.Property(x => x.UserId).HasMaxLength(64);
                e.Property(x => x.JwtId).HasMaxLength(64);
            });

            b.Entity<SharedLink>(e =>
            {
                e.ToTable("SharedLinks");
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.FileGroupId, x.Version });
                e.Property(x => x.FileGroupId).HasConversion(
                    v => v.ToString("D"), v => Guid.Parse(v));
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
