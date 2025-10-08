using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers
{
    [EnableRateLimiting("api-burst")]
    /// <summary>
    /// Пользовательский API для заявок (список моих заявок, сообщения внутри заявки).
    /// </summary>
    [ApiController]
    [Route("api/support/tickets")]
    [Authorize(Policy = "SupportDeskAccess")]
    public sealed class UserTicketsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<TicketHub> _hub;
        private readonly ILogger<UserTicketsController> _log;

        private const string TicketIdRoute = "{id:regex(^[[0-9a-fA-F]]{{8}}$)}";

        public UserTicketsController(
            ApplicationDbContext db,
            IHubContext<TicketHub> hub,
            ILogger<UserTicketsController> log)
        {
            _db = db;
            _hub = hub;
            _log = log;
        }

        private (string? userId, string? userName, bool isAgentOrAdmin) GetMe()
        {
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var uname = User.Identity?.Name;
            var isAA = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
            return (uid, uname, isAA);
        }

        private IQueryable<SupportTicket> QueryMyTickets()
        {
            var (uid, uname, isAA) = GetMe();
            if (isAA) return _db.SupportTickets.AsNoTracking();

            return _db.SupportTickets.AsNoTracking()
                .Where(t => t.OwnerUserId == uid || (t.OwnerUserId == null && t.OwnerLogin == uname));
        }

        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] string? status = "open")
        {
            var q = QueryMyTickets();
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            var list = await q
                .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                    lastMessageAt = t.Messages
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => (DateTime?)m.CreatedAt)
                        .FirstOrDefault(),
                    lastMessage = t.Messages
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => m.Body)
                        .FirstOrDefault()
                })
                .ToListAsync();

            static string Snip(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length > 160 ? s[..160] + "…" : s;
            }

            var dto = list.Select(x => new
            {
                id = x.Id,
                subject = x.Subject,
                status = x.Status,
                createdAt = x.createdAt,
                updatedAt = x.updatedAt,
                lastMessageAt = x.lastMessageAt ?? x.updatedAt ?? x.createdAt,
                lastSnippet = Snip(x.lastMessage)
            });

            return Ok(new { items = dto });
        }

        [HttpGet(TicketIdRoute)]
        public async Task<IActionResult> GetOne([FromRoute] string id)
        {
            var one = await _db.SupportTickets.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    t.CreatedAt,
                    t.UpdatedAt,
                    t.OwnerUserId,
                    t.OwnerLogin
                })
                .FirstOrDefaultAsync();

            if (one is null) return NotFound();

            var (uid, uname, isAA) = GetMe();
            var isMine = isAA || one.OwnerUserId == uid || (one.OwnerUserId == null && one.OwnerLogin == uname);
            if (!isMine) return Forbid();

            // сообщения
            var messagesRaw = await _db.SupportTicketMessages.AsNoTracking()
                .Where(m => m.TicketId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.Id,
                    role = m.AuthorRole,
                    body = m.Body,
                    createdAt = m.CreatedAt,
                    authorUserId = m.AuthorUserId
                })
                .ToListAsync();

            var msgIds = messagesRaw.Select(m => m.id).ToArray();

            // вытащим логины авторов
            var authorIds = messagesRaw.Select(m => m.authorUserId)
                                       .Where(s => s != null)
                                       .Distinct()
                                       .Cast<string>()
                                       .ToArray();

            var userNameById = (authorIds.Length == 0)
                ? new Dictionary<string, string>()
                : await _db.Users.AsNoTracking()
                    .Where(u => authorIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName })
                    .ToDictionaryAsync(x => x.Id, x => x.UserName ?? x.Id);

            string DisplayName(string? role, string? authorUserId)
            {
                userNameById.TryGetValue(authorUserId ?? "", out var login);
                if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    return login ?? one.OwnerLogin ?? "Пользователь";
                // агент/админ
                var tag = login ?? "operator";
                return $"Оператор#{tag}";
            }

            // вложения для этих сообщений (группировкой по MessageId)
            var attByMsg = (msgIds.Length == 0)
                ? new Dictionary<long, List<object>>()
                : await _db.SupportTicketAttachments.AsNoTracking()
                    .Where(s => s.TicketId == id && s.MessageId != null && msgIds.Contains(s.MessageId.Value))
                    .OrderBy(s => s.MessageId)
                    .GroupBy(s => s.MessageId!.Value)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Select(s => (object)new { id = s.Id, name = s.FileName, size = s.Size }).ToList()
                    );

            var messages = messagesRaw.Select(m => new
            {
                m.id,
                m.role,
                m.body,
                m.createdAt,
                mine = m.authorUserId == uid,
                authorName = DisplayName(m.role, m.authorUserId),
                authorAvatarUrl = m.authorUserId != null ? $"/avatars/{m.authorUserId}.jpg" : "/avatars/none.jpg",
                attachments = attByMsg.TryGetValue(m.id, out var list) ? (IEnumerable<object>)list : Array.Empty<object>()
            });

            return Ok(new { ticket = one, messages });
        }

        public sealed class NewMessageDto
        {
            public string? Body
            {
                get; set;
            }
        }

        [HttpPost(TicketIdRoute + "/messages")]
        public async Task<IActionResult> PostMessage([FromRoute] string id, [FromBody] NewMessageDto dto)
        {
            // Разрешаем пустой текст — пользователь может отправлять только вложение
            var body = dto.Body ?? string.Empty;

            if (body.Length > 1500)
                return BadRequest(new { ok = false, error = "MAX_LEN", message = "Сообщение не должно превышать 1500 символов." });

            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound();

            var (uid, uname, isAA) = GetMe();
            var isMine = isAA || t.OwnerUserId == uid || (t.OwnerUserId == null && t.OwnerLogin == uname);
            if (!isMine) return Forbid();

            var role = isAA ? "agent" : "user";

            var msg = new SupportTicketMessage
            {
                TicketId = id,
                AuthorUserId = uid,
                AuthorRole = role,
                Body = body,
                CreatedAt = DateTime.UtcNow
            };
            _db.SupportTicketMessages.Add(msg);

            t.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // авторские метаданные для live-пуша
            string authorName;
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                // логин юзера или OwnerLogin заявки
                var login = await _db.Users.Where(u => u.Id == uid).Select(u => u.UserName).FirstOrDefaultAsync();
                authorName = login ?? t.OwnerLogin ?? "Пользователь";
            }
            else
            {
                var login = await _db.Users.Where(u => u.Id == uid).Select(u => u.UserName).FirstOrDefaultAsync();
                authorName = $"Оператор#{(login ?? "operator")}";
            }
            var authorAvatarUrl = uid != null ? $"/avatars/{uid}.jpg" : "/avatars/none.jpg";

            // realtime push (включая имя/аватар)
            await _hub.Clients.Group($"ticket:{id}").SendAsync("message", new
            {
                ticketId = id,
                message = new
                {
                    id = msg.Id,
                    role = msg.AuthorRole,
                    body = msg.Body,
                    createdAt = msg.CreatedAt,
                    authorUserId = msg.AuthorUserId,
                    authorName,
                    authorAvatarUrl
                    // attachments клиент дорисует из pending, а после бинда — по API
                }
            });

            return Ok(new { ok = true, id = msg.Id, at = msg.CreatedAt });
        }

        [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/close")]
        public async Task<IActionResult> Close([FromRoute] string id)
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound();

            var isAA = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
            if (!isAA) return Forbid();

            if (t.Status == "closed")
                return Ok(new { ok = true, status = "closed", updatedAt = t.UpdatedAt });

            t.Status = "closed";
            t.UpdatedAt = DateTime.UtcNow;

            _db.SupportTicketMessages.Add(new SupportTicketMessage
            {
                TicketId = id,
                AuthorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                AuthorRole = "agent",
                Body = "Заявка закрыта оператором.",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"ticket:{id}").SendAsync("status", new
            {
                ticketId = id,
                status = "closed",
                updatedAt = t.UpdatedAt
            });

            return Ok(new { ok = true, status = "closed", updatedAt = t.UpdatedAt });
        }
    }
}
