using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;

namespace VCS_DOCs.Support.Controllers
{
    [ApiController]
    [Route("api/support/accounts")]
    [Authorize(Policy = "SupportOnly")]
    public class SupportAccountsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<UserStatusHub> _hub;
        private readonly ILogger<SupportAccountsController> _log;
        private readonly HttpClient _vdocs;
        private readonly IConfiguration _cfg;

        private string? VDocsBaseUrl => _cfg["VDocs:BaseUrl"];

        public SupportAccountsController(
            ApplicationDbContext db,
            IHubContext<UserStatusHub> hub,
            ILogger<SupportAccountsController> log,
            IHttpClientFactory http,
            IConfiguration cfg)
        {
            _db = db;
            _hub = hub;
            _log = log;
            _vdocs = http.CreateClient("VDocsBridge");
            _cfg = cfg;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? role, [FromQuery] string? q, [FromQuery] string? org)
        {
            try
            {
                var dbProjects = await _db.SupportProjects.AsNoTracking()
                    .Where(p => p.IsEnabled)
                    .OrderBy(p => p.DisplayName)
                    .Select(p => new { code = p.AppCode, name = p.DisplayName })
                    .ToListAsync();

                var projects = dbProjects.Count > 0
                    ? dbProjects
                    : new[]
                      {
                          new { code = "VSupport", name = "V-Support" },
                          new { code = "VDocs",    name = "V-DOCs"    }
                      }.ToList();

                var usersRaw = await _db.Users.AsNoTracking()
                    .Select(u => new { u.Id, u.UserName, u.FullName, u.Organization, u.Access, u.LastEntry })
                    .ToListAsync();

                var organizations = usersRaw
                    .Select(u => u.Organization ?? string.Empty)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var users = usersRaw;

                var rolesRaw = await (from ur in _db.UserRoles
                                      join r in _db.Roles on ur.RoleId equals r.Id
                                      select new
                                      {
                                          ur.UserId,
                                          r.Name
                                      })
                    .AsNoTracking()
                    .ToListAsync();

                var roleMap = rolesRaw
                    .GroupBy(x => x.UserId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(x => x.Name ?? string.Empty)
                              .Where(n => !string.IsNullOrWhiteSpace(n))
                              .Distinct(StringComparer.Ordinal)
                              .ToList(),
                        StringComparer.Ordinal);

                if (!string.IsNullOrWhiteSpace(q))
                {
                    var term = q.Trim();
                    users = users.Where(u =>
                        (u.UserName != null && u.UserName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                        (u.FullName != null && u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase))
                    ).ToList();
                }

                if (!string.IsNullOrWhiteSpace(role))
                {
                    var allowed = roleMap.Where(k => k.Value.Contains(role))
                                         .Select(k => k.Key)
                                         .ToHashSet();
                    users = users.Where(u => allowed.Contains(u.Id)).ToList();
                }

                if (!string.IsNullOrWhiteSpace(org))
                {
                    users = users.Where(u => string.Equals(u.Organization ?? string.Empty, org, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                var supportSessions = await _db.SupportUserSessions.AsNoTracking()
                    .Select(s => new { s.UserId, s.IsOnline, LastSeenUtc = (DateTime?)s.LastSeenUtc })
                    .ToDictionaryAsync(s => s.UserId, s => s);

                var userIds = users.Select(u => u.Id).Distinct().ToArray();
                var vdocsPresence = await GetVDocsPresenceAsync(userIds);

                var usersDto = users.Select(u =>
                {
                    roleMap.TryGetValue(u.Id, out var rs);

                    var sup = supportSessions.TryGetValue(u.Id, out var ss)
                        ? new
                        {
                            online = ss.IsOnline,
                            lastSeen = ss.LastSeenUtc?.ToString("yyyy-MM-dd HH:mm:ss")
                        }
                        : new
                        {
                            online = false,
                            lastSeen = (string?)null
                        };

                    vdocsPresence.TryGetValue(u.Id, out var vd);
                    var vdocs = vd.userId != null
                        ? new
                        {
                            online = vd.online,
                            lastSeen = vd.lastSeen
                        }
                        : new
                        {
                            online = false,
                            lastSeen = (string?)null
                        };

                    var presence = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["VSupport"] = sup,
                        ["VDocs"] = vdocs
                    };

                    return new
                    {
                        id = u.Id,
                        userName = u.UserName,
                        fullName = u.FullName,
                        organization = u.Organization,
                        access = u.Access,
                        roles = rs ?? new List<string>(),
                        lastEntry = (u.LastEntry as DateTime?)?.ToString("yyyy-MM-dd HH:mm:ss"),
                        presence
                    };
                });

                return Ok(new { projects, organizations, users = usersDto });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "GET /api/support/accounts failed");
                return Problem("Failed to load accounts.");
            }
        }

        [HttpPost("{id}/force-logout")]
        public async Task<IActionResult> ForceLogout([FromRoute] string id)
        {
            await KickInternalVSupport(id);
            return Ok(new { ok = true });
        }

        public sealed class KickDto
        {
            public string? Scope
            {
                get; set;
            }
        }

        [HttpPost("{id}/kick")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Kick([FromRoute] string id, [FromBody] KickDto dto)
        {
            var scope = (dto.Scope ?? "").Trim();
            if (string.IsNullOrEmpty(scope))
                return BadRequest("Scope is required (VSupport | VDocs | All).");

            if (scope.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                await KickInternalVSupport(id);
                await KickVDocsAsync(id);
                return Ok(new { ok = true });
            }

            if (scope.Equals("VSupport", StringComparison.OrdinalIgnoreCase))
            {
                await KickInternalVSupport(id);
                return Ok(new { ok = true });
            }

            if (scope.Equals("VDocs", StringComparison.OrdinalIgnoreCase))
            {
                await KickVDocsAsync(id);
                return Ok(new { ok = true });
            }

            return BadRequest("Unknown scope.");
        }

        private async Task KickInternalVSupport(string userId)
        {
            await _hub.Clients.User(userId).SendAsync("ForceLogout");
            var toRemove = _db.SupportUserConnections.Where(c => c.UserId == userId);
            _db.SupportUserConnections.RemoveRange(toRemove);
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {DateTime.UtcNow}
WHERE UserId = {userId};");
            await _db.SaveChangesAsync();
        }

        [Authorize(Roles = Roles.SupportAdmin)]
        [HttpPost("{id}/toggle-role")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleRole([FromRoute] string id, [FromBody] ToggleRoleDto dto)
        {
            if (!IsValidRole(dto.Role)) return BadRequest("Unknown role.");

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

        [Authorize(Roles = Roles.SupportAdmin)]
        [HttpPost("{id}/set-role")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetRole([FromRoute] string id, [FromBody] ToggleRoleDto dto)
        {
            if (!IsValidRole(dto.Role)) return BadRequest("Unknown role.");

            var allowed = new[] { Roles.BaseUser, Roles.SupportAgent, Roles.SupportAdmin };

            var current = await (from ur in _db.UserRoles
                                 join r in _db.Roles on ur.RoleId equals r.Id
                                 where ur.UserId == id && allowed.Contains(r.Name!)
                                 select new
                                 {
                                     ur,
                                     r.Name
                                 })
                                 .ToListAsync();

            var toRemove = current.Where(x => x.Name != dto.Role).Select(x => x.ur).ToList();
            if (toRemove.Count > 0) _db.UserRoles.RemoveRange(toRemove);

            var hasDesired = current.Any(x => x.Name == dto.Role);
            if (!hasDesired)
            {
                var roleId = await _db.Roles.Where(r => r.Name == dto.Role).Select(r => r.Id).FirstAsync();
                _db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = id, RoleId = roleId });
            }

            await _db.SaveChangesAsync();
            return Ok(new { ok = true });
        }

        [Authorize(Roles = Roles.SupportAdmin)]
        [HttpPost("{id}/toggle-access")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleAccess([FromRoute] string id)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var newAccess = user.Access == 0 ? 1 : 0;
            user.Access = newAccess;
            await _db.SaveChangesAsync();

            if (newAccess == 0)
            {
                await _hub.Clients.User(id).SendAsync("ForceLogout");
                var toRemove = _db.SupportUserConnections.Where(c => c.UserId == id);
                _db.SupportUserConnections.RemoveRange(toRemove);
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {DateTime.UtcNow}
                WHERE UserId = {id};");
                await _db.SaveChangesAsync();
                await KickVDocsAsync(id);
            }

            return Ok(new { ok = true, access = user.Access });
        }

        private static bool IsValidRole(string? role) =>
            !string.IsNullOrWhiteSpace(role) &&
            (role == Roles.BaseUser || role == Roles.SupportAgent || role == Roles.SupportAdmin);

        public sealed class ToggleRoleDto
        {
            public string? Role
            {
                get; set;
            }
        }

        private async Task<(bool ok, string? error)> KickVDocsAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(VDocsBaseUrl))
                return (false, "VDocs not configured");

            var req = new HttpRequestMessage(HttpMethod.Post, "/api/_support/kick")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { userId }), System.Text.Encoding.UTF8, "application/json")
            };

            var res = await _vdocs.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                _log.LogWarning("VDocs Kick failed: {Status} {Body}", (int)res.StatusCode, body);
                return (false, $"HTTP {(int)res.StatusCode}");
            }
            return (true, null);
        }

        private async Task<Dictionary<string, (string userId, bool online, string? lastSeen)>> GetVDocsPresenceAsync(IEnumerable<string> ids, CancellationToken ct = default)
        {
            var dict = new Dictionary<string, (string, bool, string?)>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(VDocsBaseUrl)) return dict;

            const int CHUNK = 100;
            var all = ids.Distinct(StringComparer.Ordinal).ToArray();

            for (int i = 0; i < all.Length; i += CHUNK)
            {
                var chunk = all.Skip(i).Take(CHUNK).ToArray();
                var qs = Uri.EscapeDataString(string.Join(",", chunk));
                var req = new HttpRequestMessage(HttpMethod.Get, $"/api/_support/presence?ids={qs}");

                try
                {
                    var res = await _vdocs.SendAsync(req, ct);
                    if (!res.IsSuccessStatusCode)
                    {
                        _log.LogWarning("VDocs Presence chunk failed: HTTP {Status}", (int)res.StatusCode);
                        continue;
                    }

                    using var stream = await res.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var uid = prop.Name;
                        var val = prop.Value;
                        var online = val.TryGetProperty("online", out var onProp) && onProp.GetBoolean();
                        string? lastSeen = null;
                        if (val.TryGetProperty("lastSeen", out var lsProp) && lsProp.ValueKind == JsonValueKind.String)
                            lastSeen = lsProp.GetString();

                        dict[uid] = (uid, online, lastSeen);
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "VDocs Presence chunk exception.");
                }
            }

            return dict;
        }
    }
}
