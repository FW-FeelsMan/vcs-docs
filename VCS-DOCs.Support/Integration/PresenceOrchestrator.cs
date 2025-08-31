using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Support.Integration
{
    /// <summary>
    /// Оркестратор, который по appCode находит адаптер и делегирует ему вызовы.
    /// Если адаптера нет — возвращает офлайн и игнорирует kick.
    /// </summary>
    public sealed class PresenceOrchestrator
    {
        private readonly IEnumerable<IExternalProjectAdapter> _adapters;

        public PresenceOrchestrator(IEnumerable<IExternalProjectAdapter> adapters)
        {
            _adapters = adapters;
        }

        public Task<IDictionary<string, PresenceInfo>> GetPresenceManyAsync(string appCode, IEnumerable<string> userIds, CancellationToken ct = default)
        {
            var adapter = _adapters.FirstOrDefault(a => string.Equals(a.AppCode, appCode, StringComparison.OrdinalIgnoreCase));
            if (adapter == null)
            {
                var dict = userIds
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(id => id, _ => new PresenceInfo(false, null), StringComparer.Ordinal);
                return Task.FromResult<IDictionary<string, PresenceInfo>>(dict);
            }

            return adapter.GetPresenceManyAsync(userIds, ct);
        }

        public Task KickAsync(string appCode, string userId, CancellationToken ct = default)
        {
            var adapter = _adapters.FirstOrDefault(a => string.Equals(a.AppCode, appCode, StringComparison.OrdinalIgnoreCase));
            if (adapter == null) return Task.CompletedTask;
            return adapter.KickAsync(userId, ct);
        }
    }
}
