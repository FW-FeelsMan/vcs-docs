using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<IndexModel> _log;
        private readonly UserManager<User> _userMgr;
        private readonly IOptions<UserDataPathOptions>? _userData;
        private readonly IHubContext<SupportPresenceHub> _hub;

        public User? CurrentUser
        {
            get; private set;
        }
        public string AvatarUrl { get; private set; } = "/images/default_avatar.png";

        public IndexModel(
            ApplicationDbContext db,
            ILogger<IndexModel> log,
            UserManager<User> userMgr,
            IOptions<UserDataPathOptions>? userData = null,
            IHubContext<SupportPresenceHub>? hub = null)
        {
            _db = db;
            _log = log;
            _userMgr = userMgr;
            _userData = userData;
            _hub = hub!;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return RedirectToPage("/Account/LoginSupport");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return RedirectToPage("/Account/LoginSupport");

            CurrentUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (CurrentUser is null)
                return RedirectToPage("/Account/LoginSupport");

            // Аватар как в вебе: /userdata/u_{shortId}/a/avatar.jpg
            try
            {
                var basePath = _userData?.Value?.BasePath;
                if (!string.IsNullOrWhiteSpace(basePath))
                {
                    var shortId = CurrentUser.Id.Replace("-", "");
                    if (shortId.Length >= 8) shortId = shortId[..8];

                    var physical = Path.Combine(basePath, $"u_{shortId}", "a", "avatar.jpg");
                    if (System.IO.File.Exists(physical))
                        AvatarUrl = $"/userdata/u_{shortId}/a/avatar.jpg?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Avatar resolve error (fallback to default).");
            }

            return Page();
        }

        public async Task<IActionResult> OnPostLogoutAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!string.IsNullOrEmpty(userId))
            {
                // 1) Кикнуть все активные вкладки пользователя
                try { await _hub.Clients.User(userId).SendAsync("ForceLogout"); }
                catch (Exception ex) { _log.LogDebug(ex, "Logout: ForceLogout send failed."); }

                // 2) Снести записи о соединениях (чтобы не мешали OnDisconnected гонки)
                try
                {
                    var toRemove = _db.SupportUserConnections.Where(c => c.UserId == userId);
                    _db.SupportUserConnections.RemoveRange(toRemove);
                    await _db.SaveChangesAsync();
                }
                catch (Exception ex) { _log.LogDebug(ex, "Logout: cleanup connections failed."); }

                // 3) IsOnline = 0 с ретраем на случай SQLITE_BUSY (database is locked)
                var now = DateTime.UtcNow;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions
SET IsOnline = 0, LastSeenUtc = {now}
WHERE UserId = {userId};");
                        break;
                    }
                    catch (SqliteException ex) when (ex.SqliteErrorCode == 5 && attempt < 3) // SQLITE_BUSY
                    {
                        await Task.Delay(100 * attempt);
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Logout: update session failed.");
                        break;
                    }
                }
            }

            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return RedirectToPage("/Account/LoginSupport");
        }
    }
}
