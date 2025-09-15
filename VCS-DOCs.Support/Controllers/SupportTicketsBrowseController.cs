// VCS-DOCs.Support/Controllers/SupportTicketsBrowseController.cs
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;

namespace VCS_DOCs.Support.Controllers
{
    /// <summary>
    /// Выдаёт списки заявок для операторов:
    /// - /open — открытые (с фильтрами mine/unassigned/all, поиск, организация)
    /// - /closed — закрытые
    /// - /orgs — справочник организаций по открытым тикетам
    /// </summary>
    [ApiController]
    [Route("api/support/tickets")]
    [Authorize(Policy = "SupportOnly")]
    public sealed class SupportTicketsBrowseController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SupportTicketsBrowseController> _log;

        public SupportTicketsBrowseController(ApplicationDbContext db, ILogger<SupportTicketsBrowseController> log)
        {
            _db = db;
            _log = log;
        }

        /// <summary>Строчка списка открытых заявок.</summary>
        public sealed record OpenTicketRowDto(
            string Id,
            string Subject,
            string UserLogin,
            string? Organization,
            string Wait,          // "user" | "operator" (кто писал последним)
            string OperatorLogin  // "" — если ещё нет ответа оператора
        );

        /// <summary>Строчка списка закрытых заявок.</summary>
        public sealed record ClosedTicketRowDto(
            string Id,
            string Subject,
            string UserLogin,
            string? Organization,
            DateTime CreatedAt,
            DateTime UpdatedAt
        );

        /// <summary>
        /// Открытые заявки.
        /// Фильтры:
        /// scope: all|mine|unassigned,
        /// org: точное совпадение,
        /// q: поисковая строка (id/subject/login/org).
        /// </summary>
        [HttpGet("open")]
        public async Task<IActionResult> Open([FromQuery] string? scope, [FromQuery] string? org, [FromQuery] string? q)
        {
            scope = (scope ?? "all").Trim().ToLowerInvariant();
            org = (org ?? "").Trim();
            q = (q ?? "").Trim();
            var currentOpLogin = User?.Identity?.Name ?? string.Empty;
            var qLower = q.ToLowerInvariant();

            var baseQ =
                from t in _db.SupportTickets.AsNoTracking()
                where t.Status != "closed"
                join uById in _db.Users.AsNoTracking() on t.OwnerUserId equals uById.Id into uJoin
                from u in uJoin.DefaultIfEmpty()
                let ownerLogin = (t.OwnerLogin ?? u.UserName) ?? ""
                let ownerOrg = u.Organization
                let lastRole = _db.SupportTicketMessages
                                   .AsNoTracking()
                                   .Where(m => m.TicketId == t.Id)
                                   .OrderByDescending(m => m.CreatedAt)
                                   .Select(m => m.AuthorRole)
                                   .FirstOrDefault()
                let lastAgentUserId = _db.SupportTicketMessages
                                         .AsNoTracking()
                                         .Where(m => m.TicketId == t.Id && m.AuthorRole != "user")
                                         .OrderByDescending(m => m.CreatedAt)
                                         .Select(m => m.AuthorUserId)
                                         .FirstOrDefault()
                let opLogin = _db.Users.AsNoTracking()
                                       .Where(x => x.Id == lastAgentUserId)
                                       .Select(x => x.UserName)
                                       .FirstOrDefault()
                select new
                {
                    t.Id,
                    t.Subject,
                    OwnerLogin = ownerLogin,
                    Organization = ownerOrg,
                    Wait = (lastRole == "user") ? "user" : "operator",
                    OperatorLogin = opLogin ?? "",
                    t.UpdatedAt
                };

            if (!string.IsNullOrEmpty(org))
                baseQ = baseQ.Where(x => x.Organization == org);

            if (!string.IsNullOrEmpty(qLower))
                baseQ = baseQ.Where(x =>
                    (x.Id ?? "").ToLower().Contains(qLower) ||
                    ((x.Subject ?? "").ToLower().Contains(qLower)) ||
                    ((x.OwnerLogin ?? "").ToLower().Contains(qLower)) ||
                    ((x.Organization ?? "").ToLower().Contains(qLower)));

            baseQ = scope switch
            {
                "mine" => baseQ.Where(x => (x.OperatorLogin ?? "") == currentOpLogin),
                "unassigned" => baseQ.Where(x => string.IsNullOrEmpty(x.OperatorLogin)),
                _ => baseQ
            };

            baseQ = baseQ.OrderByDescending(x => x.UpdatedAt);

            var rowsRaw = await baseQ.ToListAsync();

            var rows = rowsRaw.Select(x => new OpenTicketRowDto(
                Id: x.Id,
                Subject: x.Subject ?? "(без темы)",
                UserLogin: x.OwnerLogin,
                Organization: string.IsNullOrWhiteSpace(x.Organization) || x.Organization == "Не установлено" ? null : x.Organization,
                Wait: x.Wait,
                OperatorLogin: x.OperatorLogin ?? ""
            )).ToArray();

            return Ok(rows);
        }

        /// <summary>Закрытые заявки (с фильтром по org и поиском q).</summary>
        [HttpGet("closed")]
        public async Task<IActionResult> Closed([FromQuery] string? org, [FromQuery] string? q)
        {
            org = (org ?? "").Trim();
            q = (q ?? "").Trim();
            var qLower = q.ToLowerInvariant();

            var baseQ =
                from t in _db.SupportTickets.AsNoTracking()
                where t.Status == "closed"
                join uById in _db.Users.AsNoTracking() on t.OwnerUserId equals uById.Id into uJoin
                from u in uJoin.DefaultIfEmpty()
                let ownerLogin = (t.OwnerLogin ?? u.UserName) ?? ""
                let ownerOrg = u.Organization
                select new
                {
                    t.Id,
                    t.Subject,
                    OwnerLogin = ownerLogin,
                    Organization = ownerOrg,
                    t.CreatedAt,
                    t.UpdatedAt
                };

            if (!string.IsNullOrEmpty(org))
                baseQ = baseQ.Where(x => x.Organization == org);

            if (!string.IsNullOrEmpty(qLower))
                baseQ = baseQ.Where(x =>
                    (x.Id ?? "").ToLower().Contains(qLower) ||
                    ((x.Subject ?? "").ToLower().Contains(qLower)) ||
                    ((x.OwnerLogin ?? "").ToLower().Contains(qLower)) ||
                    ((x.Organization ?? "").ToLower().Contains(qLower)));

            baseQ = baseQ.OrderByDescending(x => x.UpdatedAt);

            var rowsRaw = await baseQ.ToListAsync();

            var rows = rowsRaw.Select(x => new ClosedTicketRowDto(
                 Id: x.Id,
                 Subject: x.Subject ?? "(без темы)",
                 UserLogin: x.OwnerLogin,
                 Organization: string.IsNullOrWhiteSpace(x.Organization) || x.Organization == "Не установлено" ? null : x.Organization,
                 CreatedAt: ((DateTime?)x.CreatedAt ?? (DateTime?)x.UpdatedAt ?? DateTime.UtcNow),
                 UpdatedAt: ((DateTime?)x.UpdatedAt ?? (DateTime?)x.CreatedAt ?? DateTime.UtcNow)
             )).ToArray();

            return Ok(rows);
        }

        /// <summary>Справочник организаций по открытым тикетам.</summary>
        [HttpGet("orgs")]
        public async Task<IActionResult> Orgs()
        {
            var orgs =
                await (from t in _db.SupportTickets.AsNoTracking()
                       where t.Status != "closed"
                       join u in _db.Users.AsNoTracking() on t.OwnerUserId equals u.Id into uJoin
                       from u in uJoin.DefaultIfEmpty()
                       select u.Organization)
                    .Where(o => !string.IsNullOrWhiteSpace(o) && o != "Не установлено")
                    .Distinct()
                    .OrderBy(o => o)
                    .ToListAsync();

            return Ok(orgs);
        }
    }
}
