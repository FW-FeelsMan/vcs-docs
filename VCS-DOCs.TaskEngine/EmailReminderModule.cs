using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Core.Notifications;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.TaskEngine;

namespace VCS_DOCs.TaskModule.EmailReminder
{
    public sealed class EmailReminderModule : ITaskModule
    {
        private readonly ApplicationDbContext _db;
        private readonly IMailSender _mailer;
        private ILogger _log = default!;
        private int _batchSize = 100;
        private TimeSpan _delay = TimeSpan.FromHours(12);

        public EmailReminderModule(ApplicationDbContext db, IMailSender mailer)
        {
            _db = db;
            _mailer = mailer;
        }

        public string Id => "support.email-reminder";
        public string Name => "Support ticket reminder emailer";

        // как часто опрашиваем БД (можно 1–5 минут, без фанатизма)
        public TimeSpan RunEvery => TimeSpan.FromMinutes(2);

        public Task InitAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct)
        {
            _log = logger;
            var section = cfg.GetSection("Modules:EmailReminder");
            var hours = section.GetValue<int?>("DelayHours") ?? 12;
            _delay = TimeSpan.FromHours(Math.Max(1, hours));
            _batchSize = Math.Max(1, section.GetValue<int?>("BatchSize") ?? 100);
            return Task.CompletedTask;
        }

        public async Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var border = now - _delay;

            // берём последние сообщения операторов по тикетам, где включены уведомления,
            // у сообщения ReminderEmailSentAt == null, и оно старше порога, и с тех пор не было ответа пользователя.
            var candidates = await _db.SupportTicketMessages
                .AsNoTracking()
                .Where(m => m.AuthorRole == "operator"
                            && m.ReminderEmailSentAt == null
                            && m.CreatedAt <= border)
                .OrderBy(m => m.CreatedAt)
                .Take(_batchSize)
                .Select(m => new {
                    m.TicketId,
                    m.CreatedAt,
                    m.Id
                })
                .ToListAsync(ct);

            if (candidates.Count == 0)
                return new TaskResult { Success = true, Message = "no candidates" };

            int sent = 0;
            foreach (var opMsg in candidates)
            {
                ct.ThrowIfCancellationRequested();

                // ещё раз быстро валидируем: включены ли уведомления и не было ли затем ответа пользователя
                var t = await _db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == opMsg.TicketId, ct);
                if (t == null || !t.EmailNotifyEnabled) continue;

                bool hasUserReplyAfter = await _db.SupportTicketMessages.AsNoTracking()
                    .AnyAsync(m => m.TicketId == opMsg.TicketId
                                   && m.AuthorRole == "user"
                                   && m.CreatedAt > opMsg.CreatedAt, ct);

                if (hasUserReplyAfter) continue;

                // Куда слать (берём ReplyToEmail из тикета; если пусто, можно попробовать Email владельца)
                var to = t.ReplyToEmail;
                if (string.IsNullOrWhiteSpace(to))
                {
                    // fallback: e-mail владельца, если хранится в Users
                    var ownerEmail = await _db.Users.AsNoTracking()
                        .Where(u => u.Id == t.OwnerUserId)
                        .Select(u => u.Email)
                        .FirstOrDefaultAsync(ct);

                    to = string.IsNullOrWhiteSpace(ownerEmail) ? null : ownerEmail;
                }
                if (string.IsNullOrWhiteSpace(to)) continue; // некуда слать

                // Письмо
                try
                {
                    var portalUrl = cfgStr(_db, "Portal:PublicBaseUrl") ?? "https://vcs-docs.support.local:7121";
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

                    await _mailer.SendAsync(to, subject, html);

                    // помечаем отправку, чтобы не слать повторно
                    await _db.Database.ExecuteSqlInterpolatedAsync($@"
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

        // маленький helper — прочитать строковый конфиг, если он вдруг нужен из БД/Environment.
        private static string? cfgStr(ApplicationDbContext db, string key)
        {
            // при желании сюда можно добавить чтение kv-таблицы конфигов из БД
            return null;
        }
    }
}
