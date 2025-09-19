using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Infrastructure.Provision;
using VCS_DOCs.Support.Infrastructure.Email; // SmtpOptions in DI
using VCS_DOCs.Support.Infrastructure.Mail;  // IMailSender
using Microsoft.Extensions.Configuration;

namespace VCS_DOCs.Support.Pages.Support
{
    public class RequestModel : PageModel
    {
        private readonly IConfiguration _cfg;
        private readonly ISupportUserProvisioning _provisioning;
        private readonly ILogger<RequestModel> _log;
        private readonly ApplicationDbContext _db;
        private readonly IMailSender _mailer;

        private readonly long _defaultStorageLimitBytes;

        public RequestModel(
            IConfiguration cfg,
            ISupportUserProvisioning provisioning,
            ILogger<RequestModel> log,
            ApplicationDbContext db,
            IMailSender mailer)
        {
            _cfg = cfg;
            _provisioning = provisioning;
            _log = log;
            _db = db;
            _mailer = mailer;

            _defaultStorageLimitBytes =
                _cfg.GetValue<long?>("Storage:DefaultLimitBytes")
                ?? 10L * 1024 * 1024 * 1024;
        }

        public bool CaptchaEnabled
        {
            get; private set;
        }
        public string CaptchaProvider { get; private set; } = "ReCaptchaV2";
        public string? CaptchaSiteKey
        {
            get; private set;
        }

        public void OnGet()
        {
            CaptchaEnabled = _cfg.GetValue<bool>("Captcha:Enabled");
            CaptchaProvider = _cfg["Captcha:Provider"] ?? "ReCaptchaV2";
            CaptchaSiteKey = _cfg["Captcha:SiteKey"];
        }

        public sealed class InputModel
        {
            public string? FullName
            {
                get; set;
            }
            public string? Login
            {
                get; set;
            }

            [EmailAddress]
            public string? ReplyTo
            {
                get; set;
            }

            [Required, MinLength(3), MaxLength(200)]
            public string Subject { get; set; } = string.Empty;

            [Required, MinLength(5), MaxLength(20_000)]
            public string Message { get; set; } = string.Empty;

            public string? CaptchaAnswer
            {
                get; set;
            }
            public string? CaptchaToken
            {
                get; set;
            }

            // Флаг подтверждения создания нового аккаунта
            public bool? ConfirmCreate
            {
                get; set;
            }
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errs = ModelState
                        .Where(kv => kv.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        );

                    return new JsonResult(new
                    {
                        success = false,
                        error = "Некорректные данные",
                        details = errs
                    })
                    {
                        StatusCode = 400
                    };
                }

                var fullName = (Input.FullName ?? "").Trim();
                var email = (Input.ReplyTo ?? "").Trim();
                var loginRaw = (Input.Login ?? "").Trim();

                // если логина нет — сгенерируем от e-mail (ограничения Identity: [a-z0-9._-], без пробелов)
                if (string.IsNullOrWhiteSpace(loginRaw) && !string.IsNullOrWhiteSpace(email) && email.Contains('@'))
                {
                    var (local, domainRoot) = SplitEmail(email);
                    var baseLogin = SanitizeLoginBase(local);
                    if (string.IsNullOrWhiteSpace(baseLogin)) baseLogin = "user";
                    loginRaw = await BuildUniqueLoginAsync(baseLogin, domainRoot);
                }

                if (string.IsNullOrWhiteSpace(loginRaw))
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = "Укажите логин или почту"
                    })
                    {
                        StatusCode = 400
                    };
                }

                // Проверим, есть ли такой пользователь
                var exists = await _db.Users.AsNoTracking()
                    .AnyAsync(u => u.NormalizedUserName == loginRaw.ToUpperInvariant());

                if (!exists && Input.ConfirmCreate != true)
                {
                    // просим подтверждение (фронт покажет диалог и повторит запрос с ConfirmCreate=true)
                    return new JsonResult(new
                    {
                        success = false,
                        code = "account_absent",
                        message = "Учетная запись с указанным логином не существует. Создать новую и отправить запрос?",
                        suggestedLogin = loginRaw
                    })
                    {
                        StatusCode = 409
                    };
                }

                _log.LogInformation("PROVISION start: login={Login} email={Email}", loginRaw, email);

                var (user, created, tempPassword) = await _provisioning.EnsureUserExistsAsync(
                    login: loginRaw,
                    email: string.IsNullOrWhiteSpace(email) ? null : email,
                    fullName: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                    organization: null,
                    department: null
                );

                _log.LogInformation("PROVISION done: id={Id} login={Login} created={Created}", user.Id, user.UserName, created);

                // Гарантируем StorageLimitBytes
                var dbUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
                if (dbUser != null && (dbUser.StorageLimitBytes == null || dbUser.StorageLimitBytes <= 0))
                {
                    dbUser.StorageLimitBytes = _defaultStorageLimitBytes;
                    await _db.SaveChangesAsync();
                    _log.LogInformation("Set StorageLimitBytes={Limit} for user {Id}", dbUser.StorageLimitBytes, dbUser.Id);
                }

                // Создаём тикет + первое сообщение
                var ticketId = NewShortId(); // 8 hex
                var now = DateTime.UtcNow;

                var t = new SupportTicket
                {
                    Id = ticketId,
                    Subject = Input.Subject?.Trim(),
                    Status = "open",
                    CreatedAt = now,
                    UpdatedAt = now,
                    OwnerUserId = user.Id,
                    OwnerLogin = user.UserName,
                    ReplyToEmail = string.IsNullOrWhiteSpace(email) ? null : email
                };

                var first = new SupportTicketMessage
                {
                    TicketId = ticketId,
                    AuthorUserId = user.Id,
                    AuthorRole = "user",
                    Body = Input.Message.Trim(),
                    CreatedAt = now
                };

                _db.SupportTickets.Add(t);
                _db.SupportTicketMessages.Add(first);
                await _db.SaveChangesAsync();

                // Письмо пользователю (если указан email)
                if (!string.IsNullOrWhiteSpace(email))
                {
                    try
                    {
                        var baseUrl = _cfg["TicketUrlTemplate"]; // например: https://vcs-docs.local:7120/Support/Tickets/{id}
                        var ticketUrl = !string.IsNullOrWhiteSpace(baseUrl)
                            ? baseUrl.Replace("{id}", WebUtility.UrlEncode(ticketId))
                            : $"#{ticketId}";

                        var subj = $"[Поддержка] Заявка № {ticketId} создана";

                        var html = BuildEmailHtml(
                            ticketId: ticketId,
                            ticketSubject: Input.Subject?.Trim() ?? "(без темы)",
                            ticketUrl: ticketUrl,
                            userLogin: user.UserName ?? "",
                            wasCreated: created,
                            tempPassword: created ? (tempPassword ?? "") : null
                        );

                        await _mailer.SendAsync(email, subj, html);
                    }
                    catch (Exception mex)
                    {
                        _log.LogWarning(mex, "Failed to send ticket email to {Email}", email);
                    }
                }

                var traceId = HttpContext.TraceIdentifier;

                return new JsonResult(new
                {
                    success = true,
                    created,               // был ли создан аккаунт
                    ticketId,              // № тикета
                    userId = user.Id,
                    login = user.UserName,
                    email = string.IsNullOrWhiteSpace(email) ? user.Email : email,
                    traceId
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Support request submission failed");
                return new JsonResult(new
                {
                    success = false,
                    error = "Внутренняя ошибка сервера"
                })
                {
                    StatusCode = 500
                };
            }
        }

        private static string NewShortId() => Guid.NewGuid().ToString("N")[..8];

        private static (string local, string? domainRoot) SplitEmail(string email)
        {
            try
            {
                var parts = email.Split('@', 2);
                var local = parts[0];
                string? domainRoot = null;
                if (parts.Length == 2)
                {
                    var dom = parts[1];
                    var dot = dom.IndexOf('.');
                    domainRoot = dot > 0 ? dom[..dot] : dom;
                }
                return (local, domainRoot);
            }
            catch { return (email, null); }
        }

        private static string SanitizeLoginBase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = Regex.Replace(raw, @"[^a-zA-Z0-9._-]+", "").ToLowerInvariant();
            s = Regex.Replace(s, @"([._-])\1{1,}", "$1").Trim('.', '_', '-');
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            if (!char.IsLetterOrDigit(s[0])) s = "u-" + s;
            return s;
        }

        private async Task<string> BuildUniqueLoginAsync(string baseLogin, string? domainRoot, int maxLen = 32)
        {
            baseLogin = TruncateClean(baseLogin, maxLen);
            var candidates = new List<string> { baseLogin };

            if (!string.IsNullOrWhiteSpace(domainRoot))
            {
                var dom = SanitizeLoginBase(domainRoot);
                if (!string.IsNullOrWhiteSpace(dom))
                    candidates.Add(WithSuffix(baseLogin, dom, maxLen));
            }

            foreach (var cand in candidates)
                if (!await ExistsUserNameAsync(cand)) return cand;

            var seed = candidates.Last();
            for (int i = 1; i <= 1000; i++)
            {
                var cand = WithSuffix(seed, i.ToString(), maxLen);
                if (!await ExistsUserNameAsync(cand)) return cand;
            }

            var tail = Convert.ToString(Random.Shared.NextInt64(), 36)![..6];
            return WithSuffix(baseLogin, tail, maxLen);
        }

        private static string TruncateClean(string s, int maxLen)
            => string.IsNullOrEmpty(s) || s.Length <= maxLen ? s : s[..maxLen];

        private static string WithSuffix(string basePart, string suffix, int maxLen)
        {
            const string sep = "-";
            var need = suffix.Length + sep.Length;
            var head = basePart;
            if (head.Length + need > maxLen)
            {
                head = head[..Math.Max(1, maxLen - need)];
                head = head.Trim('.', '_', '-');
            }
            var res = $"{head}{sep}{suffix}".Trim('.', '_', '-');
            return TruncateClean(res, maxLen);
        }

        private async Task<bool> ExistsUserNameAsync(string candidate)
        {
            var norm = candidate.ToUpperInvariant();
            return await _db.Users.AsNoTracking().AnyAsync(u => u.NormalizedUserName == norm);
        }

        private static string Html(string? s)
            => WebUtility.HtmlEncode(s ?? "");

        private static string BuildEmailHtml(string ticketId, string ticketSubject, string ticketUrl, string userLogin, bool wasCreated, string? tempPassword)
        {
            // очень простой адаптивный HTML
            var intro = wasCreated
                ? $"<p>Для вас создана учётная запись в системе поддержки.</p>"
                : $"<p>Ваше обращение принято в работу.</p>";

            var creds = wasCreated
                ? $@"<div style=""margin:12px 0;padding:12px;border:1px solid #e5e7eb;border-radius:8px;background:#f9fafb"">
                      <div style=""font-weight:700;margin-bottom:6px"">Данные для входа</div>
                      <div>Логин: <code>{Html(userLogin)}</code></div>
                      <div>Временный пароль: <code>{Html(tempPassword ?? "")}</code></div>
                      <div style=""color:#6b7280;margin-top:6px;font-size:.9rem"">Рекомендуем сменить пароль после первого входа.</div>
                    </div>"
                : "";

            return $@"
            <!doctype html>
            <html lang=""ru"">
            <head>
              <meta charset=""utf-8"">
              <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
              <title>Заявка № {Html(ticketId)}</title>
            </head>
            <body style=""font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;background:#ffffff;color:#111827;margin:0;padding:16px"">
              <div style=""max-width:640px;margin:0 auto"">
                <h2 style=""margin:0 0 8px 0"">Заявка № {Html(ticketId)} создана</h2>
                <div style=""color:#6b7280;margin-bottom:12px"">{Html(ticketSubject)}</div>
                {intro}
                <p>Вы можете открыть заявку по ссылке:<br>
                   <a href=""{Html(ticketUrl)}"">{Html(ticketUrl)}</a></p>
                {creds}
                <hr style=""border:none;border-top:1px solid #e5e7eb;margin:16px 0"">
                <div style=""color:#6b7280;font-size:.9rem"">Это автоматическое письмо. Пожалуйста, не отвечайте на него.</div>
              </div>
            </body>
            </html>";
        }
    }
}