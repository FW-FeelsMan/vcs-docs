using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Support.Integration
{
    public record PresenceInfo(bool IsOnline, DateTime? LastSeenUtc);

    public interface IExternalProjectAdapter
    {
        string AppCode
        {
            get;
        } // например, "VDocs"
        Task<IDictionary<string, PresenceInfo>> GetPresenceManyAsync(IEnumerable<string> userIds, CancellationToken ct = default);
        Task KickAsync(string userId, CancellationToken ct = default);
    }

    /// <summary>
    /// Нулевой адаптер для V-DOCs: все офлайн, kick — no-op.
    /// Замените на реальный, когда появится интеграция.
    /// </summary>
    public sealed class NullVDocsAdapter : IExternalProjectAdapter
    {
        public string AppCode => "VDocs";

        public Task<IDictionary<string, PresenceInfo>> GetPresenceManyAsync(IEnumerable<string> userIds, CancellationToken ct = default)
        {
            var dict = userIds
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, _ => new PresenceInfo(false, null), StringComparer.Ordinal);
            return Task.FromResult<IDictionary<string, PresenceInfo>>(dict);
        }

        public Task KickAsync(string userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
