using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/support/accounts")]
[Authorize(Policy = "SupportOnly")]
public class SupportAccountsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<UserStatusHub> _hub;
    private readonly ILogger<SupportAccountsController> _log;

    public SupportAccountsController(
        ApplicationDbContext db,
        IHubContext<UserStatusHub> hub,
        ILogger<SupportAccountsController> log)
    {
        _db = db;
        _hub = hub;
        _log = log;
    }

    // GET: /api/support/accounts?role=&q=
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? role, [FromQuery] string? q)
    {
        try
        {
            // 1) Пользователи
            var users = await _db.Users.AsNoTracking()
                .Select(u => new UserLite
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    FullName = u.FullName,
                    Access = u.Access,
                    LastEntry = u.LastEntry
                })
                .ToListAsync();

            // 2) Сессии (онлайн/lastSeen)
            var sessions = await _db.SupportUserSessions.AsNoTracking()
                .Select(s => new { s.UserId, s.IsOnline, s.LastSeenUtc })
                .ToDictionaryAsync(s => s.UserId, s => new Sess { IsOnline = s.IsOnline, LastSeenUtc = s.LastSeenUtc });

            // 3) Роли
            var rolesRaw = await (from ur in _db.UserRoles
                                  join r in _db.Roles on ur.RoleId equals r.Id
                                  select new
                                  {
                                      ur.UserId,
                                      r.Name
                                  })
                                 .ToListAsync();

            var roleMap = rolesRaw
                .GroupBy(x => x.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.Name ?? string.Empty)
                          .Where(n => !string.IsNullOrWhiteSpace(n))
                          .Distinct(StringComparer.Ordinal)
                          .ToList()
                );

            // 4) Поиск (в памяти: нечувствительный к регистру, безопасно)
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                users = users.Where(u =>
                           (!string.IsNullOrEmpty(u.UserName) && u.UserName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(u.FullName) && u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase))
                       )
                       .ToList();
            }

            // 5) Фильтр по роли (в памяти)
            if (!string.IsNullOrWhiteSpace(role))
            {
                var allow = roleMap
                    .Where(kvp => kvp.Value.Contains(role, StringComparer.Ordinal))
                    .Select(kvp => kvp.Key)
                    .ToHashSet(StringComparer.Ordinal);

                users = users.Where(u => allow.Contains(u.Id)).ToList();
            }

            // 6) Сборка DTO
            var result = users.Select(u =>
            {
                roleMap.TryGetValue(u.Id, out var rs);
                sessions.TryGetValue(u.Id, out var sess);
                return new
                {
                    id = u.Id,
                    userName = u.UserName,
                    fullName = u.FullName,
                    access = u.Access,
                    isOnline = sess?.IsOnline ?? false,
                    lastSeen = sess?.LastSeenUtc?.ToString("yyyy-MM-dd HH:mm:ss"),
                    lastEntry = u.LastEntry?.ToString("yyyy-MM-dd HH:mm:ss"),
                    roles = rs ?? new List<string>()
                };
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GET /api/support/accounts failed");
            return Problem("Failed to load accounts.");
        }
    }

    // POST: /api/support/accounts/{id}/force-logout  (Agent/Admin)
    [HttpPost("{id}/force-logout")]
    public async Task<IActionResult> ForceLogout([FromRoute] string id)
    {
        await _hub.Clients.User(id).SendAsync("ForceLogout");

        var toRemove = _db.SupportUserConnections.Where(c => c.UserId == id);
        _db.SupportUserConnections.RemoveRange(toRemove);

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {DateTime.UtcNow}
WHERE UserId = {id};");

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // POST: /api/support/accounts/{id}/toggle-role  (Admins only)
    [HttpPost("{id}/toggle-role")]
    [Authorize(Roles = Roles.SupportAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleRole([FromRoute] string id, [FromBody] ToggleRoleDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Role) ||
            (dto.Role != Roles.BaseUser && dto.Role != Roles.SupportAgent && dto.Role != Roles.SupportAdmin))
            return BadRequest("Unknown role.");

        var hasRole = await (from ur in _db.UserRoles
                             join r in _db.Roles on ur.RoleId equals r.Id
                             where ur.UserId == id && r.Name == dto.Role
                             select ur).AnyAsync();

        if (hasRole)
        {
            var urList = await (from ur in _db.UserRoles
                                join r in _db.Roles on ur.RoleId equals r.Id
                                where ur.UserId == id && r.Name == dto.Role
                                select ur).ToListAsync();
            _db.UserRoles.RemoveRange(urList);
        }
        else
        {
            var roleId = await _db.Roles.Where(r => r.Name == dto.Role).Select(r => r.Id).FirstAsync();
            _db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = id, RoleId = roleId });
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // POST: /api/support/accounts/{id}/toggle-access  (Admins only)
    [HttpPost("{id}/toggle-access")]
    [Authorize(Roles = Roles.SupportAdmin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAccess([FromRoute] string id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        user.Access = user.Access == 0 ? 1 : 0;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, access = user.Access });
    }

    private sealed class UserLite
    {
        public string Id { get; set; } = "";
        public string? UserName
        {
            get; set;
        }
        public string? FullName
        {
            get; set;
        }
        public int Access
        {
            get; set;
        }
        public DateTime? LastEntry
        {
            get; set;
        }
    }

    private sealed class Sess
    {
        public bool IsOnline
        {
            get; set;
        }
        public DateTime? LastSeenUtc
        {
            get; set;
        }
    }

    public sealed class ToggleRoleDto
    {
        public string? Role
        {
            get; set;
        }
    }
}
