using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Core.Notifications;
using VCS_DOCs.Data;
using VCS_DOCs.TaskEngine;

namespace VCS_DOCs.TaskModule.EmailReminder
{
    public sealed class EmailReminderModule : ITaskModule
    {
        private IServiceProvider _services = default!;
        private IConfiguration _cfg = default!;
        private ILogger _log = default!;

        private int _batchSize = 100;
        private TimeSpan _delay = TimeSpan.FromHours(12);
        private TimeSpan _runEvery = TimeSpan.FromMinutes(2);
        private string[] _operatorRoles = new[] { "operator", "agent" };
        private bool _useLocalTime = false;

        public string Id => "support.email-reminder";
        public string Name => "Support ticket reminder emailer";
        public TimeSpan RunEvery => _runEvery;

        public Task InitAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct)
        {
            _services = services;
            _cfg = cfg;
            _log = logger;

            var s = cfg.GetSection("Modules:EmailReminder");

            // частота запуска (по умолчанию 120с)
            var runEverySec = s.GetValue<int?>("RunEverySeconds");
            _runEvery = TimeSpan.FromSeconds(Math.Max(1, runEverySec ?? 120));

            // задержка до напоминания
            var delaySec = s.GetValue<int?>("DelaySeconds");
            var delayHours = s.GetValue<int?>("DelayHours");
            _delay = delaySec.HasValue
                ? TimeSpan.FromSeconds(Math.Max(1, delaySec.Value))
                : TimeSpan.FromHours(Math.Max(1, delayHours ?? 12));

            // размер пачки
            _batchSize = Math.Max(1, s.GetValue<int?>("BatchSize") ?? 100);

            // роли операторов
            var roles = s.GetSection("OperatorRoles").Get<string[]>();
            if (roles != null && roles.Length > 0)
                _operatorRoles = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray();

            // локальное время (если вдруг CreatedAt хранится в локали, обычно FALSE)
            _useLocalTime = s.GetValue<bool?>("UseLocalTime") ?? false;

            _log.LogInformation(
                "[EmailReminder] Init: Delay={Delay}s, RunEvery={RunEvery}s, BatchSize={Batch}, Roles=[{Roles}], TicketUrlTemplate={Tpl}, UseLocalTime={UseLocalTime}",
                _delay.TotalSeconds, _runEvery.TotalSeconds, _batchSize, string.Join(",", _operatorRoles),
                _cfg["TicketUrlTemplate"], _useLocalTime
            );

            TouchHeartbeat("init");
            return Task.CompletedTask;
        }
        public async Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct)
        {
            TouchHeartbeat("tick");

            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var mailer = scope.ServiceProvider.GetRequiredService<IMailSender>();

                var now = _useLocalTime ? DateTime.Now : DateTime.UtcNow;
                var border = now - _delay;

                _log.LogDebug("[EmailReminder] Tick now={Now:o} border={Border:o}", now, border);

                // Кандидаты: по одному последнему ОПЕРАТОРСКОМУ (AuthorRole <> 'user') до border и без ReminderEmailSentAt
                // Берём без GroupBy — через NOT EXISTS (стабильно для SQLite/EF)
                var latestByTicket = await db.SupportTicketMessages
                    .AsNoTracking()
                    .Where(m =>
                        m.AuthorRole != "user" &&
                        m.ReminderEmailSentAt == null &&
                        m.CreatedAt <= border &&
                        !db.SupportTicketMessages.Any(m2 =>
                            m2.TicketId == m.TicketId &&
                            m2.AuthorRole != "user" &&
                            m2.ReminderEmailSentAt == null &&
                            m2.CreatedAt <= border &&
                            m2.CreatedAt > m.CreatedAt))
                    .Select(m => new { m.TicketId, LastOpCreatedAt = m.CreatedAt })
                    .OrderBy(x => x.LastOpCreatedAt)
                    .Take(_batchSize)
                    .ToListAsync(ct);

                if (latestByTicket.Count == 0)
                {
                    _log.LogDebug("[EmailReminder] No candidates");
                    return new TaskResult { Success = true, Message = "no candidates" };
                }

                _log.LogInformation("[EmailReminder] Tickets to notify: {Count}", latestByTicket.Count);

                int sent = 0;
                foreach (var cand in latestByTicket)
                {
                    ct.ThrowIfCancellationRequested();

                    var t = await db.SupportTickets.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == cand.TicketId, ct);
                    if (t == null) { _log.LogDebug("Ticket {Id} not found", cand.TicketId); continue; }
                    if (!t.EmailNotifyEnabled) { _log.LogDebug("Ticket {Id}: notify disabled", cand.TicketId); continue; }

                    // Был ли ответ пользователя ПОСЛЕ последнего операторского?
                    bool hasUserReplyAfter = await db.SupportTicketMessages.AsNoTracking()
                        .AnyAsync(m => m.TicketId == cand.TicketId
                                       && m.AuthorRole == "user"
                                       && m.CreatedAt > cand.LastOpCreatedAt, ct);
                    if (hasUserReplyAfter) { _log.LogDebug("Ticket {Id}: user replied", cand.TicketId); continue; }

                    var to = t.ReplyToEmail;
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        to = await db.Users.AsNoTracking()
                            .Where(u => u.Id == t.OwnerUserId)
                            .Select(u => u.Email)
                            .FirstOrDefaultAsync(ct);
                    }
                    if (string.IsNullOrWhiteSpace(to)) { _log.LogDebug("Ticket {Id}: no recipient email", cand.TicketId); continue; }

                    try
                    {
                        var ticketUrlTemplate = _cfg["TicketUrlTemplate"] ??
                                                (_cfg["Portal:PublicBaseUrl"] is string b && !string.IsNullOrWhiteSpace(b)
                                                 ? $"{b.TrimEnd('/')}/Support/Tickets/{{id}}"
                                                 : "https://vcs-docs.support.local:7121/Support/Tickets/{id}");
                        var ticketUrl = ticketUrlTemplate.Replace("{id}", Uri.EscapeDataString(t.Id));

                        var subject = $"[Поддержка] Напоминание по заявке № {t.Id}";
                        var html = $@"
<!doctype html>
<html lang=""ru""><head><meta charset=""utf-8""></head>
<body style=""font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif"">
  <p>По вашей заявке № <b>{WebUtility.HtmlEncode(t.Id)}</b> оператор оставил сообщение.</p>
  <p>Ссылка на заявку: <a href=""{WebUtility.HtmlEncode(ticketUrl)}"">{WebUtility.HtmlEncode(ticketUrl)}</a></p>
  <p style=""color:#6b7280;font-size:.9rem"">Это напоминание отправлено автоматически, т.к. с момента ответа прошло более {_delay.TotalSeconds:0} секунд.</p>
</body></html>";

                        _log.LogInformation("[EmailReminder] Sending to {To} for ticket {Id}", to, t.Id);
                        await mailer.SendAsync(to, subject, html);

                        // Помечаем все операторские сообщения тикета до найденного (включая его)
                        var ts = _useLocalTime ? DateTime.Now : DateTime.UtcNow;
                        var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportTicketMessages
SET ReminderEmailSentAt = {ts}
WHERE TicketId = {cand.TicketId}
  AND ReminderEmailSentAt IS NULL
  AND CreatedAt <= {cand.LastOpCreatedAt}
  AND AuthorRole <> 'user';", ct);

                        _log.LogDebug("Marked {Rows} operator message(s) as notified for ticket {Id}", affected, cand.TicketId);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Failed to send reminder for ticket {Id}", cand.TicketId);
                    }
                }

                _log.LogInformation("[EmailReminder] Done: sent={Sent}", sent);
                return new TaskResult { Success = true, Message = $"sent={sent}" };
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[EmailReminder] Execute error");
                return new TaskResult { Success = false, Message = ex.Message };
            }
        }

        //        public async Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct)
        //        {
        //            TouchHeartbeat("tick");

        //            try
        //            {
        //                using var scope = _services.CreateScope();
        //                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        //                var mailer = scope.ServiceProvider.GetRequiredService<IMailSender>();

        //                var now = _useLocalTime ? DateTime.Now : DateTime.UtcNow;
        //                var border = now - _delay;

        //                _log.LogDebug("[EmailReminder] Tick: now={Now:o}, border={Border:o}, roles=[{Roles}]",
        //                    now, border, string.Join(",", _operatorRoles));

        //                // важное исправление: ищем по списку ролей операторов
        //                var candidates = await db.SupportTicketMessages
        //                    .AsNoTracking()
        //                    .Where(m =>
        //                        _operatorRoles.Contains(m.AuthorRole) &&
        //                        m.ReminderEmailSentAt == null &&
        //                        m.CreatedAt <= border)
        //                    .OrderBy(m => m.CreatedAt)
        //                    .Take(_batchSize)
        //                    .Select(m => new { m.TicketId, m.CreatedAt, m.Id })
        //                    .ToListAsync(ct);

        //                if (candidates.Count == 0)
        //                {
        //                    _log.LogDebug("[EmailReminder] No candidates");
        //                    return new TaskResult { Success = true, Message = "no candidates" };
        //                }

        //                _log.LogInformation("[EmailReminder] Candidates found: {Count}", candidates.Count);

        //                int sent = 0;
        //                foreach (var opMsg in candidates)
        //                {
        //                    ct.ThrowIfCancellationRequested();

        //                    var t = await db.SupportTickets.AsNoTracking()
        //                        .FirstOrDefaultAsync(x => x.Id == opMsg.TicketId, ct);

        //                    if (t == null)
        //                    {
        //                        _log.LogWarning("[EmailReminder] Ticket {Id} not found", opMsg.TicketId);
        //                        continue;
        //                    }

        //                    if (!t.EmailNotifyEnabled)
        //                    {
        //                        _log.LogDebug("[EmailReminder] Ticket {Id}: email notify disabled", opMsg.TicketId);
        //                        continue;
        //                    }

        //                    // есть ли ответ пользователя после операторского сообщения?
        //                    var hasUserReplyAfter = await db.SupportTicketMessages.AsNoTracking()
        //                        .AnyAsync(m => m.TicketId == opMsg.TicketId
        //                                       && m.AuthorRole == "user"
        //                                       && m.CreatedAt > opMsg.CreatedAt, ct);
        //                    if (hasUserReplyAfter)
        //                    {
        //                        _log.LogDebug("[EmailReminder] Ticket {Id}: user replied after operator message", opMsg.TicketId);
        //                        continue;
        //                    }

        //                    // адресат
        //                    var to = t.ReplyToEmail;
        //                    if (string.IsNullOrWhiteSpace(to))
        //                    {
        //                        to = await db.Users.AsNoTracking()
        //                            .Where(u => u.Id == t.OwnerUserId)
        //                            .Select(u => u.Email)
        //                            .FirstOrDefaultAsync(ct);
        //                    }
        //                    if (string.IsNullOrWhiteSpace(to))
        //                    {
        //                        _log.LogWarning("[EmailReminder] Ticket {Id}: no recipient email", opMsg.TicketId);
        //                        continue;
        //                    }

        //                    try
        //                    {
        //                        // ссылка на тикет
        //                        var ticketUrlTemplate = _cfg["TicketUrlTemplate"] ??
        //                                                (_cfg["Portal:PublicBaseUrl"] is string b && !string.IsNullOrWhiteSpace(b)
        //                                                    ? $"{b.TrimEnd('/')}/Support/Tickets/{{id}}"
        //                                                    : "https://vcs-docs.support.local:7121/Support/Tickets/{id}");
        //                        var ticketUrl = ticketUrlTemplate.Replace("{id}", Uri.EscapeDataString(t.Id));

        //                        var subject = $"[Поддержка] Напоминание по заявке № {t.Id}";
        //                        var html = $@"
        //<!doctype html>
        //<html lang=""ru""><head><meta charset=""utf-8""></head>
        //<body style=""font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif"">
        //  <p>По вашей заявке № <b>{WebUtility.HtmlEncode(t.Id)}</b> оператор оставил сообщение.</p>
        //  <p>Ссылка на заявку: <a href=""{WebUtility.HtmlEncode(ticketUrl)}"">{WebUtility.HtmlEncode(ticketUrl)}</a></p>
        //  <p style=""color:#6b7280;font-size:.9rem"">Это напоминание отправлено автоматически, т.к. с момента ответа прошло более {_delay.TotalSeconds:0} секунд.</p>
        //</body></html>";

        //                        _log.LogInformation("[EmailReminder] Sending to {To} for ticket {Id}", to, t.Id);
        //                        await mailer.SendAsync(to, subject, html);

        //                        // помечаем отправку (ключ — TicketId + CreatedAt операторского сообщения)
        //                        var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
        //UPDATE SupportTicketMessages
        //SET ReminderEmailSentAt = {(_useLocalTime ? DateTime.Now : DateTime.UtcNow)}
        //WHERE TicketId = {opMsg.TicketId} AND CreatedAt = {opMsg.CreatedAt};", ct);

        //                        _log.LogInformation("[EmailReminder] Marked ReminderEmailSentAt for ticket {Id} (rows={Rows})", opMsg.TicketId, affected);
        //                        sent++;
        //                    }
        //                    catch (Exception ex)
        //                    {
        //                        _log.LogWarning(ex, "[EmailReminder] Failed to send reminder for ticket {Id}", opMsg.TicketId);
        //                    }
        //                }

        //                _log.LogInformation("[EmailReminder] Done: sent={Sent}", sent);
        //                return new TaskResult { Success = true, Message = $"sent={sent}" };
        //            }
        //            catch (Exception ex)
        //            {
        //                _log.LogError(ex, "[EmailReminder] Execute error");
        //                return new TaskResult { Success = false, Message = ex.Message };
        //            }
        //        }

        private void TouchHeartbeat(string phase)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "email-reminder-heartbeat.txt");
                var line = $"{DateTime.UtcNow:O} {phase} Delay={_delay.TotalSeconds}s RunEvery={_runEvery.TotalSeconds}s Roles=[{string.Join(",", _operatorRoles)}]{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }
    }
}
