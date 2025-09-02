// VCS-DOCs.Support/Controllers/SupportTicketController.cs
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json; // <= ВАЖНО: для PostAsJsonAsync
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Infrastructure.Email;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("api/Support")]
    public class SupportTicketController : ControllerBase
    {
        private readonly HttpClient _vdocs;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userMgr;
        private readonly RoleManager<IdentityRole> _roleMgr;
        private readonly ILogger<SupportTicketController> _log;
        private readonly IMailSender _mail;
        private readonly IConfiguration _cfg;

        public SupportTicketController(
            IHttpClientFactory http,
            ApplicationDbContext db,
            UserManager<User> userMgr,
            RoleManager<IdentityRole> roleMgr,
            IMailSender mail,
            IConfiguration cfg,
            ILogger<SupportTicketController> log)
        {
            _vdocs = http.CreateClient("VDocsBridge");
            _db = db;
            _userMgr = userMgr;
            _roleMgr = roleMgr;
            _mail = mail;
            _cfg = cfg;
            _log = log;
        }

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

        [HttpPost("ticket")]
        [AllowAnonymous]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> Ticket([FromBody] TicketDto dto, CancellationToken ct)
        {
            // 1) создаём тикет в WEB (здесь же пройдет капча)
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
            catch { /* не критично */ }

            // 2) автосоздание пользователя (если login передан)
            var createdUser = await EnsureUserAndSetPasswordAsync(dto.login, dto.replyTo, dto.fullName, ct);

            // 3) письмо пользователю (если есть почта и учётка только что создана)
            if (!string.IsNullOrWhiteSpace(createdUser.email) &&
                createdUser.justCreated &&
                !string.IsNullOrEmpty(createdUser.plainPassword))
            {
                var urlTpl = _cfg["TicketUrlTemplate"];
                var link = !string.IsNullOrWhiteSpace(urlTpl) && !string.IsNullOrEmpty(ticketId)
                    ? urlTpl.Replace("{id}", ticketId!)
                    : null;

                var sb = new StringBuilder();
                sb.Append("<p>Ваше обращение принято");
                if (!string.IsNullOrEmpty(ticketId)) sb.Append($" (№ <b>{ticketId}</b>)");
                sb.Append(".</p>");

                if (!string.IsNullOrEmpty(link))
                    sb.Append($"<p>Ссылка на вашу заявку: <a href=\"{link}\">{link}</a></p>");

                sb.Append("<hr>");
                sb.Append("<p>Для доступа создан аккаунт:</p>");
                sb.Append($"<p>Логин: <b>{System.Net.WebUtility.HtmlEncode(createdUser.login)}</b><br/>");
                sb.Append($"Пароль: <b>{System.Net.WebUtility.HtmlEncode(createdUser.plainPassword!)}</b></p>");
                sb.Append("<p>Рекомендуем сменить пароль после первого входа.</p>");

                try
                {
                    _log.LogInformation(
                    "MAIL try: email={Email}, login={Login}, justCreated={Created}, pwdSet={PwdSet}",
                    createdUser.email, createdUser.login, createdUser.justCreated, !string.IsNullOrEmpty(createdUser.plainPassword));

                    await _mail.SendAsync(createdUser.email!, "VCS-DOCs: ваш аккаунт и заявка", sb.ToString(), ct);

                    _log.LogInformation("MAIL ok: sent to {Email}", createdUser.email);

                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to send account email to {Email}", createdUser.email);
                    // не падаем: тикет уже создан, учетка тоже
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
                Email = string.IsNullOrEmpty(email) ? null : email,
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

            // пароль
            var pwd = GenerateStrongPassword();
            var addPwd = await _userMgr.AddPasswordAsync(user, pwd);
            if (!addPwd.Succeeded)
            {
                _log.LogWarning("AddPassword failed for {Login}: {Errors}",
                    login, string.Join("; ", addPwd.Errors.Select(e => e.Description)));
                pwd = null; // учетка без пароля (не критично для тикета)
            }

            // базовая роль
            if (await _roleMgr.RoleExistsAsync(Roles.BaseUser))
                await _userMgr.AddToRoleAsync(user, Roles.BaseUser);

            await _db.SaveChangesAsync(ct);
            return (true, user.Id, user.UserName, user.Email, pwd);
        }
        // только для диагностики, потом удалить
        [HttpGet("debug/send-mail")]
        [AllowAnonymous]
        public async Task<IActionResult> SendTest([FromServices] IMailSender mail)
        {
            await mail.SendAsync("test@local", "Test email", "<b>It works!</b>");
            return Ok(new { ok = true });
        }

    }
}
