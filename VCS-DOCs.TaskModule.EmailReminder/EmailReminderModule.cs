using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Core.Notifications;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.TaskEngine;

namespace VCS_DOCs.TaskModule.EmailReminder
{
    public sealed class EmailReminderModule : ITaskModule
    {
        private IServiceProvider _services = default!;
        private IConfiguration _cfg = default!;
        private ILogger _log = default!;

        private int _batchSize = 100;
        private TimeSpan _replyDelay = TimeSpan.FromHours(2);   // follow-up (включены уведомления)
        private TimeSpan _idleDelay = TimeSpan.FromHours(12);   // общий простой
        private TimeSpan _runEvery = TimeSpan.FromMinutes(2);
        private string[] _operatorRoles = new[] { "operator", "agent" };
        private bool _useLocalTime = false;
        private bool _respectEmailNotifyForIdle = false;

        // === автозакрытие (опционально) ===
        private bool _autoCloseEnabled = false;
        private TimeSpan _autoCloseAfter = TimeSpan.FromHours(72);
        private bool _requireFollowUpBeforeAutoClose = true;
        private bool _autoCloseSendEmail = true;

        public string Id => "support.email-reminder";
        public string Name => "Support ticket reminder emailer";
        public TimeSpan RunEvery => _runEvery;

        public Task InitAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct)
        {
            _services = services;
            _cfg = cfg;
            _log = logger;

            var s = cfg.GetSection("Modules:EmailReminder");

            var runEverySec = s.GetValue<int?>("RunEverySeconds");
            _runEvery = TimeSpan.FromSeconds(Math.Max(1, runEverySec ?? 120));

            int? replySec = s.GetValue<int?>("ReplyDelaySeconds");
            int? replyHours = s.GetValue<int?>("ReplyDelayHours");
            _replyDelay = replySec.HasValue
                ? TimeSpan.FromSeconds(Math.Max(1, replySec.Value))
                : TimeSpan.FromHours(Math.Max(1, replyHours ?? 2));

            int? idleSec = s.GetValue<int?>("IdleDelaySeconds");
            int? idleHours = s.GetValue<int?>("IdleDelayHours") ?? s.GetValue<int?>("DelayHours");
            _idleDelay = idleSec.HasValue
                ? TimeSpan.FromSeconds(Math.Max(1, idleSec.Value))
                : TimeSpan.FromHours(Math.Max(1, idleHours ?? 12));

            _batchSize = Math.Max(1, s.GetValue<int?>("BatchSize") ?? 100);

            var roles = s.GetSection("OperatorRoles").Get<string[]>();
            if (roles != null && roles.Length > 0)
                _operatorRoles = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray();

            _useLocalTime = s.GetValue<bool?>("UseLocalTime") ?? false;
            _respectEmailNotifyForIdle = s.GetValue<bool?>("RespectEmailNotifyForIdle") ?? false;

            // --- автозакрытие ---
            _autoCloseEnabled = s.GetValue<bool?>("AutoCloseEnabled") ?? false;

            int? acSec = s.GetValue<int?>("AutoCloseAfterSeconds");
            int? acHours = s.GetValue<int?>("AutoCloseAfterHours") ?? s.GetValue<int?>("AutoCloseAfter");
            _autoCloseAfter = (acSec.HasValue && acSec.Value > 0)
                ? TimeSpan.FromSeconds(Math.Max(1, acSec.Value))
                : TimeSpan.FromHours(Math.Max(1, acHours ?? 72));

            _requireFollowUpBeforeAutoClose = s.GetValue<bool?>("RequireFollowUpBeforeAutoClose") ?? true;
            _autoCloseSendEmail = s.GetValue<bool?>("AutoCloseSendEmail") ?? true;

            var autoAfterLabel = (acSec.HasValue && acSec.Value > 0)
                ? $"{_autoCloseAfter.TotalSeconds:0}s"
                : $"{_autoCloseAfter.TotalHours:0}h";

            _log.LogInformation(
                "[EmailReminder] Init: ReplyDelay={Reply}s, IdleDelay={Idle}s, RunEvery={Every}s, Batch={Batch}, UseLocalTime={UseLocalTime}, RespectEmailNotifyForIdle={RespectIdle}, AutoCloseEnabled={AutoClose}, AutoCloseAfter={AutoAfter}, RequireFollowUpBeforeAutoClose={ReqFU}, AutoCloseSendEmail={CloseMail}",
                _replyDelay.TotalSeconds, _idleDelay.TotalSeconds, _runEvery.TotalSeconds,
                _batchSize, _useLocalTime, _respectEmailNotifyForIdle,
                _autoCloseEnabled, autoAfterLabel, _requireFollowUpBeforeAutoClose, _autoCloseSendEmail
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
                var borderReply = now - _replyDelay;
                var borderIdle = now - _idleDelay;

                _log.LogDebug("[EmailReminder] Tick now={Now:o} replyBorder={ReplyBorder:o} idleBorder={IdleBorder:o}",
                    now, borderReply, borderIdle);

                int sent = 0;

                // ==== A) FOLLOW-UP: последняя операторская, прошло replyDelay, e-mail включён, пользователя после не было ====
                var lastOpPerTicket =
                    from m in db.SupportTicketMessages.AsNoTracking()
                    where _operatorRoles.Contains(m.AuthorRole)
                    group m by m.TicketId into g
                    select new
                    {
                        TicketId = g.Key,
                        LastOpAt = g.Max(x => x.CreatedAt)
                    };

                var replyCandidates = await (
                    from lo in lastOpPerTicket
                    join t in db.SupportTickets.AsNoTracking() on lo.TicketId equals t.Id
                    join lm in db.SupportTicketMessages.AsNoTracking()
                        on new
                        {
                            lo.TicketId,
                            lo.LastOpAt
                        }
                        equals new
                        {
                            lm.TicketId,
                            LastOpAt = lm.CreatedAt
                        }
                    where t.Status != "closed"
                          && t.EmailNotifyEnabled
                          && lo.LastOpAt <= borderReply
                          && lm.ReminderEmailSentAt == null
                          && !db.SupportTicketMessages.Any(mu =>
                              mu.TicketId == lo.TicketId &&
                              mu.AuthorRole == "user" &&
                              mu.CreatedAt > lo.LastOpAt)
                    orderby lo.LastOpAt
                    select new
                    {
                        TicketId = t.Id,
                        LastOpId = lm.Id,
                        lo.LastOpAt
                    }
                )
                .Take(_batchSize)
                .ToListAsync(ct);

                _log.LogDebug("[EmailReminder] reply candidates: {Count}", replyCandidates.Count);

                if (replyCandidates.Count == 0)
                {
                    // Диагностика
                    var diag = await (
                        from t in db.SupportTickets.AsNoTracking()
                        where t.Status != "closed" && t.EmailNotifyEnabled
                        orderby (t.UpdatedAt ?? t.CreatedAt) descending
                        select new
                        {
                            t.Id,
                            LastOpAt = db.SupportTicketMessages
                                .Where(m => m.TicketId == t.Id && _operatorRoles.Contains(m.AuthorRole))
                                .Max(m => (DateTime?)m.CreatedAt),
                            LastOpReminder = (
                                from m in db.SupportTicketMessages
                                where m.TicketId == t.Id && _operatorRoles.Contains(m.AuthorRole)
                                orderby m.CreatedAt descending
                                select m.ReminderEmailSentAt
                            ).FirstOrDefault(),
                            LastUserAt = db.SupportTicketMessages
                                .Where(m => m.TicketId == t.Id && m.AuthorRole == "user")
                                .Max(m => (DateTime?)m.CreatedAt)
                        }
                    ).Take(20).ToListAsync(ct);

                    foreach (var d in diag)
                    {
                        var unnotOpOldEnough = d.LastOpAt.HasValue && d.LastOpAt.Value <= borderReply && d.LastOpReminder == null;
                        var userAfter = d.LastUserAt.HasValue && d.LastOpAt.HasValue && d.LastUserAt > d.LastOpAt;
                        _log.LogDebug("[EmailReminder][diag] {Id} lastOpAt={LastOp:o} lastOpReminder={Rem:o} lastUserAt={LastUser:o} unnotOpOldEnough={Old} userAfter={UserAfter}",
                            d.Id, d.LastOpAt, d.LastOpReminder, d.LastUserAt, unnotOpOldEnough, userAfter);
                    }
                }

                foreach (var cand in replyCandidates)
                {
                    ct.ThrowIfCancellationRequested();

                    var to = await ResolveRecipientAsync(db, cand.TicketId, ct);
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        _log.LogDebug("[EmailReminder] reply skip {Id}: no recipient", cand.TicketId);
                        continue;
                    }

                    try
                    {
                        var ticketUrl = BuildTicketUrl(cand.TicketId);
                        var subject = $"[VCS-DOCs] Новый ответ по заявке № {cand.TicketId}";
                        var html = BuildFollowUpHtml(cand.TicketId, ticketUrl, _replyDelay);

                        await mailer.SendAsync(to, subject, html);

                        var ts = _useLocalTime ? DateTime.Now : DateTime.UtcNow;
                        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                            $@"UPDATE SupportTicketMessages
                               SET ReminderEmailSentAt = {ts}
                               WHERE Id = {cand.LastOpId};", ct);

                        _log.LogDebug("[EmailReminder] reply sent for ticket {Id}; marked messageId={Mid} (rows={Rows})",
                            cand.TicketId, cand.LastOpId, affected);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "[EmailReminder] Follow-up send failed for ticket {Id}", cand.TicketId);
                    }
                }

                // ==== B) IDLE: давно нет активности ====
                var idleQuery = db.SupportTickets.AsNoTracking().Where(t => t.Status != "closed");
                if (_respectEmailNotifyForIdle) idleQuery = idleQuery.Where(t => t.EmailNotifyEnabled);

                var idleCandidates = await idleQuery
                    .Select(t => new
                    {
                        t.Id,
                        t.EmailNotifyEnabled,
                        t.LastIdleReminderAt,
                        LastMsgAt = db.SupportTicketMessages.Where(m => m.TicketId == t.Id).Max(m => (DateTime?)m.CreatedAt)
                    })
                    .Where(x =>
                        x.LastMsgAt != null &&
                        x.LastMsgAt <= borderIdle &&
                        (x.LastIdleReminderAt == null || x.LastIdleReminderAt <= borderIdle))
                    .OrderBy(x => x.LastMsgAt)
                    .Take(_batchSize)
                    .ToListAsync(ct);

                _log.LogDebug("[EmailReminder] idle candidates: {Count}", idleCandidates.Count);

                foreach (var cand in idleCandidates)
                {
                    ct.ThrowIfCancellationRequested();

                    var to = await ResolveRecipientAsync(db, cand.Id, ct);
                    if (string.IsNullOrWhiteSpace(to))
                    {
                        _log.LogDebug("[EmailReminder] idle skip {Id}: no recipient", cand.Id);
                        continue;
                    }

                    try
                    {
                        var ticketUrl = BuildTicketUrl(cand.Id);
                        var subject = $"[VCS-DOCs] Напоминание: нет активности по заявке № {cand.Id}";
                        var html = BuildIdleHtml(cand.Id, ticketUrl, _idleDelay);

                        await mailer.SendAsync(to, subject, html);

                        var ts = _useLocalTime ? DateTime.Now : DateTime.UtcNow;
                        var affected = await db.Database.ExecuteSqlInterpolatedAsync(
                            $@"UPDATE SupportTickets SET LastIdleReminderAt = {ts} WHERE Id = {cand.Id};", ct);

                        _log.LogDebug("[EmailReminder] idle sent for ticket {Id}; set LastIdleReminderAt (rows={Rows})",
                            cand.Id, affected);
                        sent++;
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "[EmailReminder] Idle send failed for ticket {Id}", cand.Id);
                    }
                }

                // ==== C) AUTO-CLOSE ====
                if (_autoCloseEnabled)
                {
                    var borderClose = now - _autoCloseAfter;

                    var autoCloseCands = await (
                        from lo in lastOpPerTicket
                        join t in db.SupportTickets on lo.TicketId equals t.Id
                        join lm in db.SupportTicketMessages on new
                        {
                            lo.TicketId,
                            lo.LastOpAt
                        }
                            equals new
                            {
                                lm.TicketId,
                                LastOpAt = lm.CreatedAt
                            }
                        where t.Status != "closed"
                              && lo.LastOpAt <= borderClose
                              && (!_requireFollowUpBeforeAutoClose || lm.ReminderEmailSentAt != null)
                              && !db.SupportTicketMessages.Any(mu =>
                                  mu.TicketId == lo.TicketId &&
                                  mu.AuthorRole == "user" &&
                                  mu.CreatedAt > lo.LastOpAt)
                        orderby lo.LastOpAt
                        select new
                        {
                            t.Id,
                            lo.LastOpAt
                        }
                    )
                    .Take(_batchSize)
                    .ToListAsync(ct);

                    _log.LogDebug("[EmailReminder] auto-close candidates: {Count}", autoCloseCands.Count);

                    foreach (var c in autoCloseCands)
                    {
                        ct.ThrowIfCancellationRequested();

                        var t = await db.SupportTickets.FirstOrDefaultAsync(x => x.Id == c.Id, ct);
                        if (t == null || t.Status == "closed") continue;

                        var ts = _useLocalTime ? DateTime.Now : DateTime.UtcNow;

                        // системная запись
                        db.SupportTicketMessages.Add(new Models.Entities.SupportTicketMessage
                        {
                            TicketId = t.Id,
                            AuthorRole = "agent",
                            Body = $"Заявка закрыта автоматически из-за отсутствия ответа более {FormatSpanHuman(_autoCloseAfter)}. Чтобы переоткрыть — просто ответьте в заявке.",
                            CreatedAt = ts
                        });

                        t.Status = "closed";
                        t.UpdatedAt = ts;

                        try
                        {
                            await db.SaveChangesAsync(ct);
                        }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "[EmailReminder] Auto-close save failed for ticket {Id}", t.Id);
                            continue;
                        }

                        if (_autoCloseSendEmail)
                        {
                            try
                            {
                                var to = await ResolveRecipientAsync(db, t.Id, ct);
                                if (!string.IsNullOrWhiteSpace(to))
                                {
                                    var ticketUrl = BuildTicketUrl(t.Id);
                                    var subject = $"[VCS-DOCs] Заявка № {t.Id} закрыта из-за отсутствия ответа";
                                    var html = BuildAutoCloseHtml(t.Id, ticketUrl, _autoCloseAfter);
                                    await mailer.SendAsync(to, subject, html);
                                }
                            }
                            catch (Exception ex)
                            {
                                _log.LogWarning(ex, "[EmailReminder] Auto-close mail failed for ticket {Id}", t.Id);
                            }
                        }
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

        // ===== Helpers =====

        private async Task<string?> ResolveRecipientAsync(ApplicationDbContext db, string ticketId, CancellationToken ct)
        {
            var to = await db.SupportTickets.AsNoTracking()
                .Where(t => t.Id == ticketId)
                .Select(t => new { t.ReplyToEmail, t.OwnerUserId })
                .FirstOrDefaultAsync(ct);

            if (to == null) return null;
            if (!string.IsNullOrWhiteSpace(to.ReplyToEmail)) return to.ReplyToEmail;

            return await db.Users.AsNoTracking()
                .Where(u => u.Id == to.OwnerUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
        }

        private string BuildTicketUrl(string ticketId)
        {
            var tpl = _cfg["TicketUrlTemplate"] ??
                      (_cfg["Portal:PublicBaseUrl"] is string b && !string.IsNullOrWhiteSpace(b)
                          ? $"{b.TrimEnd('/')}/Support/Tickets/{{id}}"
                          : "https://vcs-docs.support.local:7121/Support/Tickets/{id}");
            return tpl.Replace("{id}", Uri.EscapeDataString(ticketId));
        }

        private static string BrandStyleBlock => @"
<style>
  .mail-wrap{max-width:640px;margin:0 auto;padding:24px 20px;background:#0b1020;color:#e5e7eb;font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif}
  .card{background:#111827;border:1px solid #1f2937;border-radius:12px;padding:20px}
  .brand{display:flex;align-items:center;gap:10px;margin-bottom:12px}
  .brand .dot{width:10px;height:10px;border-radius:50%;background:#3b82f6;display:inline-block}
  .title{font-size:18px;margin:0 0 8px 0;color:#f3f4f6}
  .muted{color:#9ca3af;font-size:13px}
  .btn{display:inline-block;padding:10px 14px;border-radius:10px;background:#2563eb;color:#fff;text-decoration:none}
  .btn:visited{color:#fff}
  .row{margin:14px 0}
  a{color:#93c5fd}
</style>";

        private static string BuildFooterNote() =>
            @"<p class=""muted"">Пожалуйста, не отвечайте на это письмо — переходите по ссылке и отвечайте прямо в заявке.</p>";

        private static string FormatSpanHuman(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{Math.Round(t.TotalHours):0} ч.";
            if (t.TotalMinutes >= 1) return $"{Math.Round(t.TotalMinutes):0} мин.";
            return $"{Math.Round(t.TotalSeconds):0} сек.";
        }

        private string BuildFollowUpHtml(string ticketId, string ticketUrl, TimeSpan delay) => $@"
<!doctype html><html lang=""ru""><head><meta charset=""utf-8"">{BrandStyleBlock}</head>
<body>
  <div class=""mail-wrap"">
    <div class=""brand""><span class=""dot""></span><strong>VCS-DOCs Support</strong></div>
    <div class=""card"">
      <h1 class=""title"">Новый ответ по заявке № {WebUtility.HtmlEncode(ticketId)}</h1>
      <div class=""row"">Оператор оставил сообщение по вашей заявке.</div>
      <div class=""row""><a class=""btn"" href=""{WebUtility.HtmlEncode(ticketUrl)}"">Открыть заявку</a></div>
      <div class=""row muted"">Это напоминание отправлено автоматически спустя ~{(int)delay.TotalSeconds} сек. после ответа.</div>
      {(_autoCloseEnabled ? $@"<div class=""row muted"">Если ответа не будет, заявка закроется автоматически через ~{FormatSpanHuman(_autoCloseAfter)}.</div>" : "")}
      {BuildFooterNote()}
    </div>
  </div>
</body></html>";

        private string BuildIdleHtml(string ticketId, string ticketUrl, TimeSpan delay)
        {
            var closeWarn = _autoCloseEnabled
                ? $@" Иначе оператор закроет заявку через {FormatSpanHuman(_autoCloseAfter)}"
                : "";
            return $@"
<!doctype html><html lang=""ru""><head><meta charset=""utf-8"">{BrandStyleBlock}</head>
<body>
  <div class=""mail-wrap"">
    <div class=""brand""><span class=""dot""></span><strong>VCS-DOCs Support</strong></div>
    <div class=""card"">
      <h1 class=""title"">Давно нет активности по заявке № {WebUtility.HtmlEncode(ticketId)}</h1>
      <div class=""row"">По заявке давно не было ответов. Ответьте, пожалуйста, если вопрос ещё актуален.{closeWarn}.</div>
      <div class=""row""><a class=""btn"" href=""{WebUtility.HtmlEncode(ticketUrl)}"">Перейти к заявке</a></div>
      <div class=""row muted"">Это напоминание отправлено автоматически после ~{(int)delay.TotalSeconds} сек. простоя.</div>
      {BuildFooterNote()}
    </div>
  </div>
</body></html>";
        }

        private string BuildAutoCloseHtml(string ticketId, string ticketUrl, TimeSpan after) => $@"
<!doctype html><html lang=""ru""><head><meta charset=""utf-8"">{BrandStyleBlock}</head>
<body>
  <div class=""mail-wrap"">
    <div class=""brand""><span class=""dot""></span><strong>VCS-DOCs Support</strong></div>
    <div class=""card"">
      <h1 class=""title"">Заявка № {WebUtility.HtmlEncode(ticketId)} закрыта</h1>
      <div class=""row"">Мы не получили ответ в течение ~{FormatSpanHuman(after)}, поэтому заявка закрыта автоматически.</div>
      <div class=""row""><a class=""btn"" href=""{WebUtility.HtmlEncode(ticketUrl)}"">Открыть заявку</a></div>
      <div class=""row muted"">Чтобы переоткрыть — просто напишите ответ в этой заявке.</div>
      {BuildFooterNote()}
    </div>
  </div>
</body></html>";

        private void TouchHeartbeat(string phase)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "email-reminder-heartbeat.txt");
                var line = $"{DateTime.UtcNow:O} {phase} ReplyDelay={_replyDelay.TotalSeconds}s IdleDelay={_idleDelay.TotalSeconds}s AutoClose={_autoCloseEnabled}/{_autoCloseAfter.TotalSeconds}s RunEvery={_runEvery.TotalSeconds}s Roles=[{string.Join(",", _operatorRoles)}]{Environment.NewLine}";
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch { /* ignore */ }
        }
    }
}
