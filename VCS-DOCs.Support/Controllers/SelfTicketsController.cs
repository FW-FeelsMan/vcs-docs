using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/self")]
[Authorize(Policy = "SupportDeskAccess")]
public sealed class SelfTicketsController : ControllerBase
{
    private readonly IHubContext<TicketHub> _hub;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SelfTicketsController> _log;
    private readonly IConfiguration _cfg;

    public SelfTicketsController(
        ApplicationDbContext db,
        ILogger<SelfTicketsController> log,
        IConfiguration cfg,
        IHubContext<TicketHub> hub)
    {
        _db = db;
        _log = log;
        _cfg = cfg;
        _hub = hub;
    }

    public sealed class NotifyToggleDto
    {
        public string TicketId { get; set; } = "";
        public bool Enabled
        {
            get; set;
        }
    }

    [HttpPost("notify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetNotify([FromBody] NotifyToggleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TicketId))
            return BadRequest(new { ok = false, error = "no_id" });

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var login = User.Identity?.Name;

        var t = await _db.SupportTickets
            .FirstOrDefaultAsync(x =>
                x.Id == dto.TicketId &&
                (x.OwnerUserId == uid || (x.OwnerUserId == null && x.OwnerLogin == login)));

        if (t == null)
            return NotFound(new { ok = false, error = "ticket_not_found" });

        t.EmailNotifyEnabled = dto.Enabled;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, enabled = t.EmailNotifyEnabled });
    }

    // DTOs
    public sealed record UserOpenTicketDto(
        string Id,
        string Subject,
        string Wait,        // "user" | "operator" (кто писал последним)
        DateTime CreatedAt,
        DateTime UpdatedAt,
        bool Notify,
        int? AutoCloseEtaSec // секунды до авто-закрытия; null — если неприменимо
    );

    public sealed record UserClosedTicketDto(
        string Id,
        string Subject,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private async Task<(string? userId, string? login)> GetCurrentUserAsync()
    {
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var login = User.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(uid))
            return (uid, login);

        if (!string.IsNullOrWhiteSpace(login))
        {
            var norm = login.ToUpperInvariant();
            var foundId = await _db.Users
                .AsNoTracking()
                .Where(u => u.NormalizedUserName == norm)
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrWhiteSpace(foundId))
                return (foundId, login);
        }

        return (null, login);
    }

    [HttpGet("open")]
    public async Task<IActionResult> Open()
    {
        var (uid, login) = await GetCurrentUserAsync();
        if (uid is null && login is null) return Ok(Array.Empty<UserOpenTicketDto>());

        // тест-конфиг автозакрытия
        var autoCloseEnabled = _cfg.GetValue<bool?>("Modules:EmailReminder:AutoCloseEnabled") ?? false;
        var autoCloseHours = _cfg.GetValue<int?>("Modules:EmailReminder:AutoCloseAfterHours") ?? 72;
        var autoCloseSeconds = _cfg.GetValue<int?>("Modules:EmailReminder:AutoCloseAfterSeconds");
        TimeSpan autoCloseAfter =
            (autoCloseSeconds.HasValue && autoCloseSeconds.Value > 0)
                ? TimeSpan.FromSeconds(autoCloseSeconds.Value)
                : TimeSpan.FromHours(Math.Max(1, autoCloseHours));
        var now = DateTime.UtcNow;

        var q = _db.SupportTickets
            .AsNoTracking()
            .Where(t =>
                t.Status != "closed" &&
                (
                    (!string.IsNullOrEmpty(t.OwnerUserId) && t.OwnerUserId == uid) ||
                    (!string.IsNullOrEmpty(t.OwnerLogin) && t.OwnerLogin == login)
                ))
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                t.UpdatedAt,
                t.EmailNotifyEnabled,

                // последняя реплика (кто писал)
                Last = _db.SupportTicketMessages
                    .AsNoTracking()
                    .Where(m => m.TicketId == t.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.AuthorRole, m.CreatedAt })
                    .FirstOrDefault(),

                // максимум по operator/agent и по user отдельно
                LastOpAt = _db.SupportTicketMessages
                    .AsNoTracking()
                    .Where(m => m.TicketId == t.Id && m.AuthorRole != "user")
                    .Max(m => (DateTime?)m.CreatedAt),

                LastUserAt = _db.SupportTicketMessages
                    .AsNoTracking()
                    .Where(m => m.TicketId == t.Id && m.AuthorRole == "user")
                    .Max(m => (DateTime?)m.CreatedAt)
            });

        var rowsRaw = await q.ToListAsync();

        var rows = rowsRaw.Select(x =>
        {
            var wait = (string.Equals(x.Last?.AuthorRole, "user", StringComparison.OrdinalIgnoreCase)) ? "user" : "operator";

            int? etaSec = null;
            if (autoCloseEnabled && wait == "operator" && x.LastOpAt.HasValue)
            {
                // пользователь "должен ответить": считаем дедлайн от последней operator-реплики
                var deadline = x.LastOpAt.Value + autoCloseAfter;
                var left = deadline - now;
                if (left.TotalSeconds <= 0) etaSec = 0;
                else etaSec = (int)Math.Round(left.TotalSeconds);
            }

            return new UserOpenTicketDto(
                Id: x.Id,
                Subject: x.Subject ?? "(без темы)",
                Wait: wait,
                CreatedAt: x.CreatedAt,
                UpdatedAt: x.UpdatedAt ?? x.CreatedAt,
                Notify: x.EmailNotifyEnabled,
                AutoCloseEtaSec: etaSec
            );
        }).ToArray();

        return Ok(rows);
    }

    [HttpGet("closed")]
    public async Task<IActionResult> Closed()
    {
        var (uid, login) = await GetCurrentUserAsync();
        if (uid is null && login is null) return Ok(Array.Empty<UserClosedTicketDto>());

        var q = _db.SupportTickets
            .AsNoTracking()
            .Where(t =>
                t.Status == "closed" &&
                (
                    (!string.IsNullOrEmpty(t.OwnerUserId) && t.OwnerUserId == uid) ||
                    (!string.IsNullOrEmpty(t.OwnerLogin) && t.OwnerLogin == login)
                ))
            .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                t.UpdatedAt
            });

        var rowsRaw = await q.ToListAsync();

        var rows = rowsRaw.Select(x =>
            new UserClosedTicketDto(
                Id: x.Id,
                Subject: x.Subject ?? "(без темы)",
                CreatedAt: x.CreatedAt,
                UpdatedAt: x.UpdatedAt ?? x.CreatedAt
            )).ToArray();

        return Ok(rows);
    }

    // -------- создание --------
    public sealed class NewTicketDto
    {
        [Required, MinLength(3), MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MinLength(5), MaxLength(20000)]
        public string Message { get; set; } = string.Empty;

        public string? ReplyTo
        {
            get; set;
        }
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] NewTicketDto dto)
    {
        var (uid, login) = await GetCurrentUserAsync();
        if (string.IsNullOrWhiteSpace(uid) && string.IsNullOrWhiteSpace(login))
            return Unauthorized(new { ok = false, error = "not_auth" });

        if (!ModelState.IsValid)
            return BadRequest(new { ok = false, error = "bad_input" });

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == uid || u.NormalizedUserName == (login ?? "").ToUpperInvariant())
            .Select(u => new { u.Id, u.UserName, u.Email })
            .FirstOrDefaultAsync();

        var ownerId = user?.Id ?? uid;
        var ownerLogin = user?.UserName ?? login;

        if (string.IsNullOrWhiteSpace(ownerId))
            return BadRequest(new { ok = false, error = "owner_not_found" });

        var replyTo = string.IsNullOrWhiteSpace(dto.ReplyTo) ? (user?.Email ?? null) : dto.ReplyTo;

        var ticketId = Guid.NewGuid().ToString("N")[..8];
        var now = DateTime.UtcNow;

        var t = new SupportTicket
        {
            Id = ticketId,
            Subject = dto.Subject.Trim(),
            Status = "open",
            CreatedAt = now,
            UpdatedAt = now,
            OwnerUserId = ownerId,
            OwnerLogin = ownerLogin,
            ReplyToEmail = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo
        };

        var first = new SupportTicketMessage
        {
            TicketId = ticketId,
            AuthorUserId = ownerId,
            AuthorRole = "user",
            Body = dto.Message.Trim(),
            CreatedAt = now
        };

        try
        {
            _db.SupportTickets.Add(t);
            _db.SupportTicketMessages.Add(first);
            await _db.SaveChangesAsync();

            // ==== realtime: оповещаем операторов о новой заявке ====
            try
            {
                await _hub.Clients.All.SendAsync("created", new
                {
                    id = ticketId,
                    subject = t.Subject,
                    userLogin = ownerLogin,
                    organization = (string?)null, // подставь, если знаешь организацию
                    wait = "user",
                    assignedUserId = (string?)null
                });
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SignalR 'created' push failed for ticket {TicketId}", ticketId);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create self-ticket for {OwnerId}/{OwnerLogin}", ownerId, ownerLogin);
            return StatusCode(500, new { ok = false, error = "save_failed" });
        }
        //await _hub.Clients.All.SendAsync("created", new
        //{
        //    id = ticketId,
        //    subject = t.Subject,
        //    userLogin = ownerLogin ?? "",
        //    organization = "",            
        //    wait = "user",                
        //    assignedUserId = t.AssignedUserId 
        //}, HttpContext.RequestAborted);

        return Ok(new
        {
            ok = true,
            ticketId,
            subject = t.Subject,
            createdAt = t.CreatedAt
        });
    }
}