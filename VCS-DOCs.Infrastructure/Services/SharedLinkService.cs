using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Infrastructure.Services
{
    /// <summary>
    /// Uses only IUploadDbContext (no direct reference to Web layer),
    /// so there is no circular dependency.
    /// </summary>
    public class SharedLinkService : ISharedLinkService
    {
        private readonly IUploadDbContext _db;

        public SharedLinkService(IUploadDbContext db)
        {
            _db = db;
        }

        public async Task<SharedLink> CreateAsync(
            string ownerShort,
            Guid fileGroupId,
            int version,
            int ttlHours,
            int? maxDownloads,
            bool requireAuth,
            CancellationToken ct = default)
        {
            var exp = DateTimeOffset.UtcNow.AddHours(ttlHours).ToUnixTimeSeconds();

            var entity = new SharedLink
            {
                Id = Guid.NewGuid(),
                FileGroupId = fileGroupId,
                Version = version,
                Exp = exp,
                MaxDownloads = maxDownloads,
                Downloads = 0,
                RequireAuth = requireAuth,
                CreatedBy = ownerShort,
                CreatedAt = DateTime.UtcNow
            };

            _db.SharedLinks.Add(entity);
            await _db.SaveChangesAsync(ct);
            return entity;
        }

        public async Task<(SharedLink? link, string? error)> TryConsumeAsync(Guid id, CancellationToken ct = default)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var link = await _db.SharedLinks.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (link == null) return (null, "not_found");
            if (link.Exp <= now) return (null, "expired");
            if (link.MaxDownloads.HasValue && link.Downloads >= link.MaxDownloads.Value) return (null, "limit_reached");

            link.Downloads += 1;
            await _db.SaveChangesAsync(ct);
            return (link, null);
        }

        public Task<SharedLink?> GetAsync(Guid id, CancellationToken ct = default)
            => _db.SharedLinks.FirstOrDefaultAsync(x => x.Id == id, ct);
    }
}