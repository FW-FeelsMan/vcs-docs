using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using VCS_DOCs.Infrastructure.Data;     // ApplicationDbContext
using VCS_DOCs.TaskEngine;              // ITaskModule / TaskContext / TaskResult
using VCS_DOCs.Support.Hubs;            // TicketHub (для realtime-пуша)

namespace VCS_DOCs.Tickets.AutoAssign
{
    public sealed class AutoAssignModule : ITaskModule
    {
        public string Id => "tickets:auto-assign";
        public string Name => "Tickets Auto-Assign";

        public TimeSpan RunEvery => _runEvery;
        private TimeSpan _runEvery = TimeSpan.FromSeconds(60);

        private IServiceProvider? _services;
        private IConfiguration? _cfg;
        private ILogger? _log;

        public Task InitAsync(IServiceProvider services, IConfiguration configuration, ILogger logger, CancellationToken ct)
        {
            _services = services;
            _cfg = configuration;
            _log = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

            // Разрешаем переопределять период как в "Modules:AutoAssign", так и в "AutoAssign"
            var s = configuration.GetSection("Modules:AutoAssign");
            if (!s.Exists()) s = configuration.GetSection("AutoAssign");

            var sec = s.GetValue<int?>("RunEverySeconds");
            if (sec is > 0) _runEvery = TimeSpan.FromSeconds(sec.Value);

            return Task.CompletedTask;
        }

        public async Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct = default)
        {
            if (_services is null || _cfg is null)
                throw new InvalidOperationException("Module is not initialized");

            var log = _log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

            // Настройки: читаем на каждом тике (можно менять без перезапуска)
            IConfigurationSection conf = _cfg.GetSection("Modules:AutoAssign");
            if (!conf.Exists()) conf = _cfg.GetSection("AutoAssign");

            var maxPerAgent = Math.Max(1, conf.GetValue<int?>("MaxPerAgent") ?? 999_999);
            var batchLimit = Math.Max(1, conf.GetValue<int?>("BatchLimit") ?? 100);
            var includeAdmins = conf.GetValue<bool?>("IncludeAdmins") ?? true;
            var roleAgent = conf.GetValue<string>("RoleAgent") ?? "SupportAgent";
            var roleAdmin = conf.GetValue<string>("RoleAdmin") ?? "SupportAdmin";

            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var now = DateTime.UtcNow;

            // 1) Список доступных операторов (Id)
            var agentIds = await (
                from u in db.Users
                join ur in db.UserRoles on u.Id equals ur.UserId
                join r in db.Roles on ur.RoleId equals r.Id
                where r.Name == roleAgent || (includeAdmins && r.Name == roleAdmin)
                select u.Id
            ).Distinct().ToListAsync(ct);

            if (agentIds.Count == 0)
            {
                log.LogInformation("[AutoAssign] нет доступных операторов — пропуск.");
                return new TaskResult { Success = true, Message = "no agents" };
            }

            // 2) Текущая нагрузка по операторам
            var load = await db.SupportTickets
                .AsNoTracking()
                .Where(t => t.Status != "closed" && t.AssignedUserId != null && agentIds.Contains(t.AssignedUserId))
                .GroupBy(t => t.AssignedUserId!)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Id, x => x.Count, ct);

            foreach (var id in agentIds)
                if (!load.ContainsKey(id)) load[id] = 0;

            // 3) Очередь неназначенных тикетов (старые первыми)
            var queue = await db.SupportTickets
                .AsNoTracking()
                .Where(t => t.Status != "closed" && t.AssignedUserId == null)
                .OrderBy(t => t.UpdatedAt ?? t.CreatedAt)
                .Select(t => new { t.Id, t.CreatedAt, t.UpdatedAt }) // берём только то, что нужно
                .Take(batchLimit)
                .ToListAsync(ct);

            if (queue.Count == 0)
            {
                log.LogDebug("[AutoAssign] нечего назначать.");
                return new TaskResult { Success = true, Message = "nothing to assign" };
            }

            // 4) Раздача: по минимальной нагрузке, с атомарным UPDATE (защита от гонок)
            int assigned = 0;
            var rng = new Random();
            var orderedAgents = agentIds
                .OrderBy(id => load[id])
                .ThenBy(_ => rng.Next())
                .ToList();

            var assignedPairs = new List<(string TicketId, string UserId)>(); // для realtime-пуша

            foreach (var cand in queue)
            {
                ct.ThrowIfCancellationRequested();

                string? best = null;
                int bestLoad = int.MaxValue;

                foreach (var id in orderedAgents)
                {
                    var c = load[id];
                    if (c >= maxPerAgent) continue;
                    if (c < bestLoad) { bestLoad = c; best = id; if (c == 0) break; }
                }

                if (best == null) break; // все переполнены

                // АТОМАРНАЯ попытка назначить:
                // назначим только если тикет всё ещё открыт и без AssignedUserId
                var affected = await db.SupportTickets
                    .Where(t => t.Id == cand.Id && t.Status != "closed" && t.AssignedUserId == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.AssignedUserId, best)
                        .SetProperty(t => t.AssignedByUserId, (string?)null)
                        .SetProperty(t => t.AssignedAt, now)
                        .SetProperty(t => t.AssignmentMode, "auto")
                        .SetProperty(t => t.UpdatedAt, now),
                        ct);

                if (affected > 0)
                {
                    // получилось: обновим локальную нагрузку и список пушей
                    load[best] = bestLoad + 1;
                    assigned++;
                    assignedPairs.Add((cand.Id, best));

                    // переупорядочим агентов с учётом новой нагрузки
                    orderedAgents = orderedAgents
                        .OrderBy(id => load[id])
                        .ThenBy(_ => rng.Next())
                        .ToList();
                }
                // если affected == 0 — кто-то опередил, просто пропускаем
            }

            // 5) realtime-пуш в хаб (если есть, и есть что пушить)
            if (assignedPairs.Count > 0)
            {
                try
                {
                    var hub = scope.ServiceProvider.GetRequiredService<IHubContext<TicketHub>>();
                    foreach (var (ticketId, userId) in assignedPairs)
                    {
                        await hub.Clients.All.SendAsync("assigned", new
                        {
                            ticketId,
                            assignedUserId = userId,
                            assignedAt = now,
                            assignmentMode = "auto"
                        }, ct);
                    }
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "[AutoAssign] realtime push failed");
                }
            }

            log.LogInformation("[AutoAssign] назначено {Count} тикетов.", assigned);
            return new TaskResult { Success = true, Message = $"assigned={assigned}" };
        }
    }
}