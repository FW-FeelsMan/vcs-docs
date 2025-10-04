using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Data;
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
            if (isAA)
            {
                // Агенты/админы видят всё — пригодится для их панелей позже
                return _db.SupportTickets.AsNoTracking();
            }

            // Базовый пользователь — только свои заявки (по Id или, если его ещё не связали, по логину)
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

            var messages = await _db.SupportTicketMessages.AsNoTracking()
                .Where(m => m.TicketId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.Id,
                    role = m.AuthorRole,
                    body = m.Body,
                    createdAt = m.CreatedAt,
                    mine = m.AuthorUserId == uid
                })
                .ToListAsync();

            return Ok(new
            {
                ticket = one,
                messages
            });
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
            var body = (dto.Body ?? "").Trim();
            if (string.IsNullOrEmpty(body)) return BadRequest(new { ok = false, error = "Пустое сообщение" });

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

            // Push в комнату заявки
            //await _hub.Clients.Group($"ticket:{id}").SendAsync("message", new
            //{
            //    ticketId = id,
            //    message = new
            //    {
            //        id = msg.Id,
            //        role = msg.AuthorRole,
            //        body = msg.Body,
            //        createdAt = msg.CreatedAt,
            //        mine = true
            //    }
            //});
            await _hub.Clients.Group($"ticket:{id}").SendAsync("message", new
            {
                ticketId = id,
                message = new
                {
                    id = msg.Id,
                    role = msg.AuthorRole,      
                    body = msg.Body,
                    createdAt = msg.CreatedAt,
                    authorUserId = msg.AuthorUserId 
                }
            });

            return Ok(new { ok = true, id = msg.Id, at = msg.CreatedAt });
        }
        // POST /api/support/tickets/{id}/close
        [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/close")]
        public async Task<IActionResult> Close([FromRoute] string id)
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound();

            // только агент/админ закрывают
            var isAA = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
            if (!isAA) return Forbid();

            if (t.Status == "closed")
                return Ok(new { ok = true, status = "closed", updatedAt = t.UpdatedAt });

            t.Status = "closed";
            t.UpdatedAt = DateTime.UtcNow;

            _db.SupportTicketMessages.Add(new SupportTicketMessage
            {
                TicketId = id,
                AuthorUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
                AuthorRole = "agent",
                Body = "Заявка закрыта оператором.",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            // realtime: оповестим обе стороны
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
