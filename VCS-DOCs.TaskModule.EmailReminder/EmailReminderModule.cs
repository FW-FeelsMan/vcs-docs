using System.Net;
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

        public string Id => "support.email-reminder";
        public string Name => "Support ticket reminder emailer";

        // как часто опрашиваем БД (1–5 минут)
        public TimeSpan RunEvery => TimeSpan.FromMinutes(2);

        public EmailReminderModule()
        {
        }

        public Task InitAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct)
        {
            _services = services;
            _cfg = cfg;
            _log = logger;

            var section = cfg.GetSection("Modules:EmailReminder");
            var hours = section.GetValue<int?>("DelayHours") ?? 12;
            _delay = TimeSpan.FromHours(Math.Max(1, hours));
            _batchSize = Math.Max(1, section.GetValue<int?>("BatchSize") ?? 100);

            return Task.CompletedTask;
        }

        public async Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct)
        {            
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var mailer = scope.ServiceProvider.GetRequiredService<IMailSender>();

            var now = DateTime.UtcNow;
            var border = now - _delay;

            var candidates = await db.SupportTicketMessages
                .AsNoTracking()
                .Where(m => m.AuthorRole == "operator"
                            && m.ReminderEmailSentAt == null
                            && m.CreatedAt <= border)
                .OrderBy(m => m.CreatedAt)
                .Take(_batchSize)
                .Select(m => new { m.TicketId, m.CreatedAt, m.Id })
                .ToListAsync(ct);

            if (candidates.Count == 0)
                return new TaskResult { Success = true, Message = "no candidates" };

            int sent = 0;
            foreach (var opMsg in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var t = await db.SupportTickets.AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == opMsg.TicketId, ct);
                if (t == null || !t.EmailNotifyEnabled) continue;

                bool hasUserReplyAfter = await db.SupportTicketMessages.AsNoTracking()
                    .AnyAsync(m => m.TicketId == opMsg.TicketId
                                   && m.AuthorRole == "user"
                                   && m.CreatedAt > opMsg.CreatedAt, ct);
                if (hasUserReplyAfter) continue;

                var to = t.ReplyToEmail;
                if (string.IsNullOrWhiteSpace(to))
                {
                    to = await db.Users.AsNoTracking()
                        .Where(u => u.Id == t.OwnerUserId)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync(ct);

                    if (string.IsNullOrWhiteSpace(to)) continue;
                }

                try
                {
                    var portalUrl = _cfg["Portal:PublicBaseUrl"] ?? "https://vcs-docs.support.local:7121";
                    var ticketUrl = $"{portalUrl.TrimEnd('/')}/Support/Tickets/{Uri.EscapeDataString(t.Id)}";

                    var subject = $"[Поддержка] Напоминание по заявке № {t.Id}";
                    var html = $@"
                    <!doctype html>
                    <html lang=""ru"">
                    <head><meta charset=""utf-8""></head>
                    <body style=""font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif"">
                      <p>По вашей заявке № <b>{WebUtility.HtmlEncode(t.Id)}</b> оператор оставил сообщение.</p>
                      <p>Ссылка на заявку: <a href=""{WebUtility.HtmlEncode(ticketUrl)}"">{WebUtility.HtmlEncode(ticketUrl)}</a></p>
                      <p style=""color:#6b7280;font-size:.9rem"">Это напоминание отправлено автоматически, т.к. с момента ответа прошло более {_delay.TotalHours:0} часов.</p>
                    </body>
                    </html>";

                    await mailer.SendAsync(to, subject, html);

                    await db.Database.ExecuteSqlInterpolatedAsync($@"
                        UPDATE SupportTicketMessages
                        SET ReminderEmailSentAt = {DateTime.UtcNow}
                        WHERE TicketId = {opMsg.TicketId} AND CreatedAt = {opMsg.CreatedAt};", ct);

                    sent++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send reminder for ticket {Id}", opMsg.TicketId);
                }
            }

            return new TaskResult { Success = true, Message = $"sent={sent}" };
        }
    }
}
