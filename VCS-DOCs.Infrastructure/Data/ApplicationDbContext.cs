using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Infrastructure.Data
{
    public partial class ApplicationDbContext : IdentityDbContext<User>, IUploadDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // ---- Presence / SignalR ----
        public DbSet<SupportUserSession> SupportUserSessions => Set<SupportUserSession>();
        public DbSet<SupportUserConnection> SupportUserConnections => Set<SupportUserConnection>();

        // ---- Uploads / Settings / Links (IUploadDbContext) ----
        public DbSet<FileUploadSessionModel> FileUploadSessions { get; set; } = default!;
        public DbSet<ServerSettingModel> ServerSettings { get; set; } = default!;
        public DbSet<SharedLink> SharedLinks { get; set; } = default!;

        // ---- Support ----
        public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
        public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
        public DbSet<SupportProject> SupportProjects => Set<SupportProject>();
        public DbSet<SupportTicketAttachment> SupportTicketAttachments => Set<SupportTicketAttachment>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            base.OnModelCreating(b);

            // ------- Presence -------
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

            // ------- Uploads / Settings / Links -------
            b.Entity<FileUploadSessionModel>(e =>
            {
                e.ToTable("FileUploadSessions");
                e.HasKey(x => x.FileId);

                e.Property(x => x.FileId).ValueGeneratedNever(); // у тебя Guid приходит извне
                e.Property(x => x.UserId).HasMaxLength(64);
                e.Property(x => x.OriginalFileName).HasMaxLength(260);
                e.Property(x => x.FileHash).HasMaxLength(128);
                e.Property(x => x.Status).HasMaxLength(24);
                e.Property(x => x.UpdatedAt);

                e.Property(x => x.FileGroupId);

                // полезные индексы
                e.HasIndex(x => new { x.UserId, x.UpdatedAt });
                e.HasIndex(x => new { x.FileGroupId, x.Version });
            });

            b.Entity<ServerSettingModel>(e =>
            {
                e.ToTable("ServerSettings");
                e.HasKey(x => x.Key);
                e.Property(x => x.Key).HasMaxLength(128);
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

            // ------- Support Tickets / Messages -------
            b.Entity<SupportTicket>(e =>
            {
                e.ToTable("SupportTickets");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasMaxLength(32);
                e.Property(x => x.Status).HasMaxLength(16).HasDefaultValue("open");
                e.HasIndex(x => x.Status);
                e.HasIndex(x => new { x.OwnerUserId, x.OwnerLogin });
                e.Property(t => t.EmailNotifyEnabled).HasDefaultValue(true);
            });

            b.Entity<SupportTicketMessage>(e =>
            {
                e.ToTable("SupportTicketMessages");
                e.HasKey(x => x.Id);
                e.Property(x => x.TicketId).HasMaxLength(32).IsRequired();
                e.Property(x => x.AuthorRole).HasMaxLength(16);
                e.HasIndex(x => x.TicketId);
                e.HasOne(x => x.Ticket)
                 .WithMany(t => t.Messages)
                 .HasForeignKey(x => x.TicketId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(m => new { m.TicketId, m.CreatedAt });
            });

            b.Entity<SupportProject>(e =>
            {
                e.ToTable("SupportProjects");
                e.HasKey(x => x.Id);

                e.Property(x => x.AppCode).HasMaxLength(64).IsRequired();
                e.HasIndex(x => x.AppCode).IsUnique();

                e.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
                e.Property(x => x.BaseUrl).HasMaxLength(512);
                e.Property(x => x.ApiKey).HasMaxLength(256);
                e.Property(x => x.IsEnabled).HasDefaultValue(true);

                e.Property(x => x.CreatedUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.Capabilities).HasConversion<long>();
                e.Property(x => x.MetadataJson).HasColumnType("TEXT");
            });

            // ------- Attachments (под твою модель) -------
            b.Entity<SupportTicketAttachment>(e =>
            {
                e.ToTable("SupportTicketAttachments");
                e.HasKey(x => x.Id);

                e.Property(x => x.Id).ValueGeneratedOnAdd(); // long Identity
                e.Property(x => x.TicketId).HasMaxLength(32).IsRequired();
                e.Property(x => x.MessageId); // long? (nullable)

                e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
                e.Property(x => x.ContentType).HasMaxLength(128);
                e.Property(x => x.Size);
                e.Property(x => x.StorageKey).HasMaxLength(512).IsRequired();

                e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
                e.Property(x => x.CreatedByUserId).HasMaxLength(64);
                e.Property(x => x.CreatedByRole).HasMaxLength(32);

                e.HasIndex(x => x.TicketId);
                e.HasIndex(x => new { x.TicketId, x.MessageId });

                // FK на тикет (без нав.свойств)
                e.HasOne<SupportTicket>()
                 .WithMany()
                 .HasForeignKey(x => x.TicketId)
                 .OnDelete(DeleteBehavior.Cascade);

                // FK на сообщение опционален — при удалении сообщения обнуляем ссылку
                e.HasOne<SupportTicketMessage>()
                 .WithMany()
                 .HasForeignKey(x => x.MessageId)
                 .OnDelete(DeleteBehavior.SetNull);
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
                optionsBuilder.UseSqlite("Data Source=VCSDocs.db");
        }
    }
}