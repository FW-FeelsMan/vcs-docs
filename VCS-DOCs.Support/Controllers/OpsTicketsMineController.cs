using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsTicketsMineController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public OpsTicketsMineController(ApplicationDbContext db)
    {
        _db = db;
    }

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("my-closed")]
    public async Task<IActionResult> MyClosed([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] string? q = null)
    {
        var me = CurrentUserId();
        if (string.IsNullOrWhiteSpace(me)) return Forbid();

        var query =
            from t in _db.SupportTickets.AsNoTracking()
            where t.Status == "closed" && t.AssignedUserId == me
            join u in _db.Users.AsNoTracking() on t.OwnerUserId equals u.Id into uJoin
            from u in uJoin.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                ClosedAt = t.UpdatedAt ?? t.CreatedAt,
                OwnerLogin = t.OwnerLogin ?? u.UserName,
                Organization = u.Organization,
                ResolutionMinutes = EF.Functions.DateDiffMinute(t.CreatedAt, t.UpdatedAt ?? t.CreatedAt),
                OperatorReplies = _db.SupportTicketMessages.AsNoTracking()
                    .Count(m => m.TicketId == t.Id && m.AuthorRole != "user"),
                UserReplies = _db.SupportTicketMessages.AsNoTracking()
                    .Count(m => m.TicketId == t.Id && m.AuthorRole == "user")
            };

        if (from.HasValue)
        {
            query = query.Where(x => x.ClosedAt >= from.Value.ToUniversalTime());
        }

        if (to.HasValue)
        {
            query = query.Where(x => x.ClosedAt <= to.Value.ToUniversalTime());
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.Id + " " + (x.Subject ?? string.Empty) + " " + (x.OwnerLogin ?? string.Empty) + " " + (x.Organization ?? string.Empty))
                .ToLower()
                .Contains(s));
        }

        var rows = await query
            .OrderByDescending(x => x.ClosedAt)
            .ToListAsync();

        var result = rows.Select(x => new
        {
            id = x.Id,
            subject = x.Subject ?? "(без темы)",
            organization = x.Organization ?? string.Empty,
            closedAt = x.ClosedAt,
            resolutionMinutes = x.ResolutionMinutes,
            replies = new
            {
                user = x.UserReplies,
                op = x.OperatorReplies
            },
            createdAt = x.CreatedAt
        });

        return Ok(result);
    }
}
