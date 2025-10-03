// VCS-DOCs.Support/Controllers/SelfTicketsController.cs
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/self")]
[Authorize(Policy = "SupportDeskAccess")]
public sealed class SelfTicketsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SelfTicketsController> _log;

    public SelfTicketsController(ApplicationDbContext db, ILogger<SelfTicketsController> log)
    {
        _db = db;
        _log = log;
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

    // -------- DTOs (для списка) --------
    public sealed record UserOpenTicketDto(
        string Id,
        string Subject,
        string Wait,        // "user" | "operator" (кто писал последним)
        DateTime CreatedAt,
        DateTime UpdatedAt,
        bool Notify);

    public sealed record UserClosedTicketDto(
        string Id,
        string Subject,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    // -------- helpers --------
    private async Task<(string? userId, string? login)> GetCurrentUserAsync()
    {
        // 1) пробуем взять Id из клейма (NameIdentifier) — самый надёжный вариант
        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var login = User.Identity?.Name;

        if (!string.IsNullOrWhiteSpace(uid))
            return (uid, login);

        // 2) фолбэк: если только Name (логин), найдём Id в БД
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

        return (null, login); // нет Id — дальше Create вернёт вежливую ошибку
    }

    // -------- списки --------
    [HttpGet("open")]
    public async Task<IActionResult> Open()
    {
        var (uid, login) = await GetCurrentUserAsync();
        if (uid is null && login is null) return Ok(Array.Empty<UserOpenTicketDto>());

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
                Last = _db.SupportTicketMessages
             .AsNoTracking()
             .Where(m => m.TicketId == t.Id)
             .OrderByDescending(m => m.CreatedAt)
             .Select(m => new { m.AuthorRole })
             .FirstOrDefault()
            });

        var rowsRaw = await q.ToListAsync();

        var rows = rowsRaw.Select(x =>
            new UserOpenTicketDto(
                Id: x.Id,
                Subject: x.Subject ?? "(без темы)",
                Wait: (string.Equals(x.Last?.AuthorRole, "user", StringComparison.OrdinalIgnoreCase)) ? "user" : "operator",
                CreatedAt: ((DateTime?)x.CreatedAt ?? (DateTime?)x.UpdatedAt ?? DateTime.UtcNow),
                UpdatedAt: ((DateTime?)x.UpdatedAt ?? (DateTime?)x.CreatedAt ?? DateTime.UtcNow),
                Notify: x.EmailNotifyEnabled
            )).ToArray();

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
                CreatedAt: ((DateTime?)x.CreatedAt ?? (DateTime?)x.UpdatedAt ?? DateTime.UtcNow),
                UpdatedAt: ((DateTime?)x.UpdatedAt ?? (DateTime?)x.CreatedAt ?? DateTime.UtcNow)
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
        } // опционально переопределить e-mail
    }

    [HttpPost("tickets")]
    public async Task<IActionResult> Create([FromBody] NewTicketDto dto)
    {
        var (uid, login) = await GetCurrentUserAsync();
        if (string.IsNullOrWhiteSpace(uid) && string.IsNullOrWhiteSpace(login))
            return Unauthorized(new { ok = false, error = "not_auth" });

        if (!ModelState.IsValid)
            return BadRequest(new { ok = false, error = "bad_input" });

        // возьмём пользователя (чтобы точно был Id и корректный e-mail)
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
            AuthorUserId = ownerId,               // <— теперь точно НЕ null
            AuthorRole = "user",
            Body = dto.Message.Trim(),
            CreatedAt = now
        };

        try
        {
            _db.SupportTickets.Add(t);
            _db.SupportTicketMessages.Add(first);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to create self-ticket for {OwnerId}/{OwnerLogin}", ownerId, ownerLogin);
            return StatusCode(500, new { ok = false, error = "save_failed" });
        }

        return Ok(new
        {
            ok = true,
            ticketId,
            subject = t.Subject,
            createdAt = t.CreatedAt
        });
    }
}
