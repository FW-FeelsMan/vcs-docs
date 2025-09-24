// VCS-DOCs.Support/Controllers/SupportIntakeController.cs
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Notifications;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Controllers
{
    /// <summary>
    /// Принимает обращения с публичной формы /Support/Request:
    /// - Создаёт тикет (через web-сервис и локальный upsert на всякий случай)
    /// - При необходимости создаёт аккаунт пользователю и отправляет письмо
    /// - Возвращает номер тикета и краткую квитанцию
    /// </summary>
    [EnableRateLimiting("api-burst")]
    [ApiController]
    [Route("api/Support")] // сохраняем текущий маршрут, чтобы ничего не ломалось
    public class SupportIntakeController : ControllerBase
    {
        private readonly HttpClient _vdocs;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userMgr;
        private readonly RoleManager<IdentityRole> _roleMgr;
        private readonly ILogger<SupportIntakeController> _log;
        private readonly IMailSender _mail;
        private readonly IConfiguration _cfg;

        public SupportIntakeController(
            IHttpClientFactory http,
            ApplicationDbContext db,
            UserManager<User> userMgr,
            RoleManager<IdentityRole> roleMgr,
            IMailSender mail,
            IConfiguration cfg,
            ILogger<SupportIntakeController> log)
        {
            _vdocs = http.CreateClient("VDocsBridge");
            _db = db;
            _userMgr = userMgr;
            _roleMgr = roleMgr;
            _mail = mail;
            _cfg = cfg;
            _log = log;
        }

        /// <summary>
        /// Входящая модель обращения с формы.
        /// </summary>
        public sealed class TicketDto
        {
            public string? fullName
            {
                get; set;
            }
            public string? login
            {
                get; set;
            }
            public string replyTo { get; set; } = "";
            public string subject { get; set; } = "";
            public string message { get; set; } = "";
            public string? code
            {
                get; set;
            }
            public string? originalPath
            {
                get; set;
            }
            public string? traceId
            {
                get; set;
            }
            public string? userAgent
            {
                get; set;
            }
            public string? captchaId
            {
                get; set;
            }
            public string? captchaAnswer
            {
                get; set;
            }
        }

        // === simple validators ===
        private static readonly Regex LoginRegex = new(@"^[a-zA-Z0-9]{1,20}$", RegexOptions.Compiled);
        private static bool IsValidEmail(string? s) =>
            !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s, @"^[^\s@]+@[^\s@]+\.[^\s@]+$");

        private static IList<string> ValidateTicket(TicketDto dto)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(dto.replyTo) || !IsValidEmail(dto.replyTo))
                errors.Add("Укажите корректную почту для ответа (например, user@example.com).");
            if (!string.IsNullOrWhiteSpace(dto.login) && !LoginRegex.IsMatch(dto.login))
                errors.Add("Логин может содержать только латинские буквы и цифры, до 20 символов.");
            if (string.IsNullOrWhiteSpace(dto.subject) || dto.subject.Length < 3)
                errors.Add("Тема обращения слишком короткая.");
            if (string.IsNullOrWhiteSpace(dto.message) || dto.message.Length < 10)
                errors.Add("Текст обращения слишком короткий.");
            return errors;
        }

        /// <summary>
        /// Создать тикет из публичной формы. Опционально — создать аккаунт и отправить письмо.
        /// </summary>
        [HttpPost("ticket")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Ticket([FromBody] TicketDto dto, CancellationToken ct)
        {
            var errors = ValidateTicket(dto);
            if (errors.Count > 0)
                return BadRequest(new { success = false, errors });

            // 1) создаём тикет в WEB (там же проходит капча)
            var webRes = await _vdocs.PostAsJsonAsync("/api/Support/ticket", dto, ct);
            var webBody = await webRes.Content.ReadAsStringAsync(ct);
            if (!webRes.IsSuccessStatusCode)
            {
                _log.LogWarning("WEB ticket create failed: {Status} {Body}", (int)webRes.StatusCode, webBody);
                return StatusCode((int)webRes.StatusCode, webBody);
            }

            string? ticketId = null;
            try
            {
                using var doc = JsonDocument.Parse(webBody);
                if (doc.RootElement.TryGetProperty("ticketId", out var t) && t.ValueKind == JsonValueKind.String)
                    ticketId = t.GetString();
            }
            catch { /* ok */ }

            if (!string.IsNullOrEmpty(ticketId))
            {
                try { await UpsertTicketFromWebAsync(ticketId!, dto, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Upsert ticket failed for {TicketId}", ticketId); }
            }

            // 2) автопровижининг пользователя (если передан login)
            var createdUser = await EnsureUserAndSetPasswordAsync(dto.login, dto.replyTo, dto.fullName, ct);

            // 3) письмо пользователю
            var ticketUrl = BuildTicketUrl(ticketId);
            if (!string.IsNullOrWhiteSpace(createdUser.email))
            {
                string subject, html;

                if (createdUser.justCreated && !string.IsNullOrEmpty(createdUser.plainPassword))
                {
                    subject = "VCS-DOCs: ваш аккаунт и заявка";
                    var sb = new StringBuilder();
                    sb.Append("<p>Ваше обращение принято");
                    if (!string.IsNullOrEmpty(ticketId)) sb.Append($" (№ <b>{ticketId}</b>)");
                    sb.Append(".</p>");
                    if (!string.IsNullOrEmpty(ticketUrl))
                        sb.Append($@"<p>Ссылка на вашу заявку: <a href=""{ticketUrl}"">{ticketUrl}</a></p>");
                    sb.Append("<hr><p>Для доступа создан аккаунт:</p>");
                    sb.Append($"<p>Логин: <b>{System.Net.WebUtility.HtmlEncode(createdUser.login)}</b><br/>");
                    sb.Append($"Пароль: <b>{System.Net.WebUtility.HtmlEncode(createdUser.plainPassword!)}</b></p>");
                    sb.Append("<p>Рекомендуем сменить пароль после первого входа.</p>");
                    html = sb.ToString();
                }
                else
                {
                    subject = "VCS-DOCs: обращение принято";
                    var resetUrl = await TryBuildResetUrlAsync(createdUser.userId);
                    var sb = new StringBuilder();
                    sb.Append("<p>Ваше обращение принято");
                    if (!string.IsNullOrEmpty(ticketId)) sb.Append($" (№ <b>{ticketId}</b>)");
                    sb.Append(".</p>");
                    if (!string.IsNullOrEmpty(ticketUrl))
                        sb.Append($@"<p>Ссылка на вашу заявку: <a href=""{ticketUrl}"">{ticketUrl}</a></p>");
                    sb.Append($"<p>Аккаунт с логином <b>{System.Net.WebUtility.HtmlEncode(createdUser.login)}</b> уже существует.</p>");
                    if (!string.IsNullOrEmpty(resetUrl))
                        sb.Append($@"<p>Забыли пароль? <a href=""{resetUrl}"">Сбросить пароль</a>.</p>");
                    else
                        sb.Append("<p>Забыли пароль? Воспользуйтесь ссылкой «Забыли пароль?» на странице входа.</p>");
                    html = sb.ToString();
                }

                try
                {
                    await _mail.SendAsync(createdUser.email!, subject, html, ct);
                    _log.LogInformation("MAIL ok: sent to {Email}", createdUser.email);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send mail to {Email}", createdUser.email);
                }
            }

            // 4) ответ фронту (без пароля!)
            return Ok(new
            {
                success = true,
                ticketId,
                created = createdUser.justCreated,
                userId = createdUser.userId,
                login = createdUser.login,
                email = createdUser.email
            });
        }

        /// <summary>
        /// Защитный upsert тикета по номеру, если web-сервис отдал ticketId.
        /// </summary>
        private async Task UpsertTicketFromWebAsync(string ticketId, TicketDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(ticketId)) return;

            var login = (dto.login ?? "").Trim();
            var email = (dto.replyTo ?? "").Trim();
            var subject = (dto.subject ?? "").Trim();
            var body = (dto.message ?? "").Trim();

            var t = await _db.SupportTickets.Include(x => x.Messages)
                .FirstOrDefaultAsync(x => x.Id == ticketId, ct);

            if (t == null)
            {
                string? ownerId = null;
                if (!string.IsNullOrEmpty(login))
                {
                    var u = await _userMgr.FindByNameAsync(login);
                    if (u != null) ownerId = u.Id;
                }

                t = new SupportTicket
                {
                    Id = ticketId,
                    Subject = string.IsNullOrEmpty(subject) ? "Без темы" : subject,
                    Status = "open",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    OwnerUserId = ownerId,
                    OwnerLogin = string.IsNullOrEmpty(ownerId) ? (string.IsNullOrEmpty(login) ? null : login) : null,
                    ReplyToEmail = string.IsNullOrEmpty(email) ? null : email
                };

                _db.SupportTickets.Add(t);
            }

            if (!string.IsNullOrWhiteSpace(body) && !(t.Messages?.Any() ?? false))
            {
                var first = new SupportTicketMessage
                {
                    TicketId = t.Id,
                    AuthorUserId = t.OwnerUserId,
                    AuthorRole = "user",
                    Body = body,
                    CreatedAt = DateTime.UtcNow
                };
                _db.SupportTicketMessages.Add(first);
                t.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
        }

        /// <summary>Собрать ссылку на карточку тикета (если задан шаблон в конфиге).</summary>
        private string? BuildTicketUrl(string? ticketId)
        {
            if (string.IsNullOrEmpty(ticketId)) return null;
            var tpl = _cfg["TicketUrlTemplate"];
            return string.IsNullOrWhiteSpace(tpl) ? null : tpl.Replace("{id}", ticketId);
        }

        /// <summary>Попытаться построить ссылку на сброс пароля.</summary>
        private async Task<string?> TryBuildResetUrlAsync(string? userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            var user = await _userMgr.FindByIdAsync(userId);
            if (user == null) return null;

            var baseUrl = (_cfg["WebBaseUrl"] ?? _cfg["VDocs:BaseUrl"])?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return null;

            try
            {
                var token = await _userMgr.GeneratePasswordResetTokenAsync(user);
                return $"{baseUrl}/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
            }
            catch { return null; }
        }

        /// <summary>Сгенерировать сильный временный пароль.</summary>
        private static string GenerateStrongPassword(int length = 16)
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%^&*";
            Span<byte> buf = stackalloc byte[length];
            RandomNumberGenerator.Fill(buf);
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(alphabet[buf[i] % alphabet.Length]);
            return sb.ToString();
        }

        /// <summary>
        /// Убедиться, что пользователь существует; при необходимости — создать и задать пароль.
        /// </summary>
        private async Task<(bool justCreated, string? userId, string? login, string? email, string? plainPassword)>
            EnsureUserAndSetPasswordAsync(string? login, string? email, string? fullName, CancellationToken ct)
        {
            login = (login ?? "").Trim();
            email = (email ?? "").Trim();

            if (string.IsNullOrEmpty(login))
                return (false, null, null, email, null);

            var existing = await _userMgr.FindByNameAsync(login);
            if (existing != null)
                return (false, existing.Id, existing.UserName, existing.Email, null);

            var user = new User
            {
                UserName = login,
                Email = IsValidEmail(email) ? email : null,
                FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                CreatedAt = DateTime.UtcNow,
                Access = 1,
                IsDeleted = false,
                EmailConfirmed = false
            };

            var createRes = await _userMgr.CreateAsync(user);
            if (!createRes.Succeeded)
            {
                _log.LogWarning("User provision failed for {Login}: {Errors}",
                    login, string.Join("; ", createRes.Errors.Select(e => e.Description)));
                return (false, null, login, email, null);
            }

            var pwd = GenerateStrongPassword();
            var addPwd = await _userMgr.AddPasswordAsync(user, pwd);
            if (!addPwd.Succeeded)
            {
                _log.LogWarning("AddPassword failed for {Login}: {Errors}",
                    login, string.Join("; ", addPwd.Errors.Select(e => e.Description)));
                pwd = null;
            }

            if (await _roleMgr.RoleExistsAsync(Roles.BaseUser))
                await _userMgr.AddToRoleAsync(user, Roles.BaseUser);

            await _db.SaveChangesAsync(ct);
            return (true, user.Id, user.UserName, user.Email, pwd);
        }

        /// <summary>Тест почты (диагностика SMTP).</summary>
        [HttpGet("debug/send-mail")]
        [AllowAnonymous]
        public async Task<IActionResult> SendTest([FromServices] IMailSender mail)
        {
            await mail.SendAsync("test@local", "Test email", "<b>It works!</b>");
            return Ok(new { ok = true });
        }
    }
}
