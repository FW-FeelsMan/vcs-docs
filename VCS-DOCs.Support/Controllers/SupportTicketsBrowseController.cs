// Support/Controllers/SupportTicketsBrowseController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class SupportTicketsBrowseController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public SupportTicketsBrowseController(ApplicationDbContext db)
    {
        _db = db;
    }

    private string? CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static string WhoWaits(string? lastRole) =>
        string.Equals(lastRole, "user", StringComparison.OrdinalIgnoreCase) ? "user" : "operator";

    /// <summary>
    /// Открытые заявки для операторов.
    /// Параметры:
    ///   scope: all | mine | unassigned
    ///   org: точное совпадение организации
    ///   q: поиск по Id/Subject/UserLogin/Organization
    /// </summary>
    [HttpGet("open")]
    public async Task<IActionResult> Open([FromQuery] string? scope = "all",
                                          [FromQuery] string? org = null,
                                          [FromQuery] string? q = null)
    {
        var me = CurrentUserId();

        // Базовый запрос по открытым тикетам
        var qBase =
            from t in _db.SupportTickets.AsNoTracking()
            where t.Status != "closed"
            join u in _db.Users.AsNoTracking() on t.OwnerUserId equals u.Id into uJoin
            from u in uJoin.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.Subject,
                t.CreatedAt,
                t.UpdatedAt,
                OwnerLogin = t.OwnerLogin ?? u.UserName,
                Organization = u.Organization, // колонка у ваших пользователей
                t.AssignedUserId,

                Last = _db.SupportTicketMessages
                    .AsNoTracking()
                    .Where(m => m.TicketId == t.Id)
                    .OrderByDescending(m => m.CreatedAt)
                    .Select(m => new { m.AuthorRole, m.CreatedAt, m.AuthorUserId })
                    .FirstOrDefault(),

                // логин последнего оператора в переписке — чисто как fallback
                LastOpLogin = (
                    from m in _db.SupportTicketMessages.AsNoTracking()
                    join op in _db.Users.AsNoTracking() on m.AuthorUserId equals op.Id into opj
                    from op in opj.DefaultIfEmpty()
                    where m.TicketId == t.Id && m.AuthorRole != "user"
                    orderby m.CreatedAt descending
                    select op.UserName
                ).FirstOrDefault()
            };

        // Фильтры
        if (!string.IsNullOrWhiteSpace(org))
            qBase = qBase.Where(x => x.Organization == org);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLowerInvariant();
            qBase = qBase.Where(x =>
                (x.Id + " " + (x.Subject ?? "") + " " + (x.OwnerLogin ?? "") + " " + (x.Organization ?? ""))
                    .ToLower()
                    .Contains(s));
        }

        scope = (scope ?? "all").Trim().ToLowerInvariant();
        if (scope == "mine" && !string.IsNullOrWhiteSpace(me))
            qBase = qBase.Where(x => x.AssignedUserId == me);
        else if (scope == "unassigned")
            qBase = qBase.Where(x => x.AssignedUserId == null);

        var rows = await qBase
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .ToListAsync();

        var result = rows.Select(x => new
        {
            id = x.Id,
            subject = x.Subject ?? "(без темы)",
            userLogin = x.OwnerLogin ?? "",
            organization = x.Organization ?? "",
            wait = WhoWaits(x.Last?.AuthorRole),
            assignedUserId = x.AssignedUserId,
            // для совместимости со старым фронтом: покажем логин назначенного, иначе последнего оператора
            operatorLogin = x.AssignedUserId != null
                ? _db.Users.AsNoTracking().Where(u => u.Id == x.AssignedUserId).Select(u => u.UserName).FirstOrDefault()
                : x.LastOpLogin
        }).ToArray();

        return Ok(result);
    }

    /// <summary>
    /// Справочник организаций из открытых тикетов.
    /// </summary>
    [HttpGet("orgs")]
    public async Task<IActionResult> Orgs()
    {
        var orgs = await (
                from t in _db.SupportTickets.AsNoTracking()
                where t.Status != "closed"
                join u in _db.Users.AsNoTracking() on t.OwnerUserId equals u.Id into uJoin
                from u in uJoin.DefaultIfEmpty()
                select u.Organization
            )
            .Where(o => !string.IsNullOrWhiteSpace(o) && o != "Не установлено")
            .Distinct()
            .OrderBy(o => o)
            .ToListAsync();

        return Ok(orgs);
    }
}
