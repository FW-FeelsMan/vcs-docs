using System;
using System.Threading;
using System.Threading.Tasks;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Core.Interfaces
{
    public interface ISharedLinkService
    {
        Task<SharedLink> CreateAsync(
            string ownerShort,
            Guid fileGroupId,
            int version,
            int ttlHours,
            int? maxDownloads,
            bool requireAuth,
            CancellationToken ct = default);

        /// <summary>
        /// Checks expiration & limits, and increments Downloads if allowed.
        /// Returns (link, null) if consumption is allowed.
        /// </summary>
        Task<(SharedLink? link, string? error)> TryConsumeAsync(Guid id, CancellationToken ct = default);

        Task<SharedLink?> GetAsync(Guid id, CancellationToken ct = default);
    }
}