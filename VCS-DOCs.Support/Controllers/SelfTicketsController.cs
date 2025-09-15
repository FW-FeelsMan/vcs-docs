using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;

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

    public sealed record UserOpenTicketDto(
        string Id,
        string Subject,
        string Wait,        // "user" | "operator" (кто написал ПОСЛЕДНИМ)
        DateTime CreatedAt,
        DateTime UpdatedAt,
        bool Notify);

    public sealed record UserClosedTicketDto(
        string Id,
        string Subject,
        DateTime CreatedAt,
        DateTime UpdatedAt);

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
            .OrderByDescending(t => t.UpdatedAt)
            .Select(t => new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                t.UpdatedAt,
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
                Wait: (x.Last?.AuthorRole == "operator") ? "operator" : "user",
                CreatedAt: ((DateTime?)x.CreatedAt ?? (DateTime?)x.UpdatedAt ?? DateTime.UtcNow),
                UpdatedAt: ((DateTime?)x.UpdatedAt ?? (DateTime?)x.CreatedAt ?? DateTime.UtcNow),
                Notify: false
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
            .OrderByDescending(t => t.UpdatedAt)
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

    private async Task<(string? userId, string? login)> GetCurrentUserAsync()
    {
        var login = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(login)) return (null, null);

        var norm = login.ToUpperInvariant();
        var userId = await _db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedUserName == norm)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();

        return (userId, login);
    }
}
