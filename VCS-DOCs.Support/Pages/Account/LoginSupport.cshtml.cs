using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Pages.Account
{
    public class LoginSupportModel : PageModel
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<SupportPresenceHub> _hub;

        public LoginSupportModel(
            SignInManager<User> signInManager,
            UserManager<User> userManager,
            ApplicationDbContext db,
            IHubContext<SupportPresenceHub> hub)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _hub = hub;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage
        {
            get; set;
        }

        public class InputModel
        {
            [Required] public string Username { get; set; } = "";
            [Required] public string Password { get; set; } = "";
            public bool ForceLogin
            {
                get; set;
            }
            public string? ReturnUrl
            {
                get; set;
            }
            public string? HardwareId
            {
                get; set;
            }
        }

        public void OnGet(string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl) || returnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase))
                returnUrl = Url.Content("~/");
            Input.ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrEmpty(Input.ReturnUrl) || !Url.IsLocalUrl(Input.ReturnUrl) || Input.ReturnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase))
                Input.ReturnUrl = Url.Content("~/");

            if (!ModelState.IsValid)
            {
                ErrorMessage = string.Join("; ",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage ?? e.Exception?.Message));
                return Page();
            }

            var user = await _userManager.FindByNameAsync(Input.Username);
            if (user == null || user.IsDeleted || user.Access == 0)
            {
                ErrorMessage = "Неверное имя пользователя или аккаунт не активирован.";
                return Page();
            }

            var allowed =
                           await _userManager.IsInRoleAsync(user, Roles.BaseUser)
                        || await _userManager.IsInRoleAsync(user, Roles.SupportAgent)
                        || await _userManager.IsInRoleAsync(user, Roles.SupportAdmin);

            if (!allowed)
            {
                ErrorMessage = "Доступ в портал поддержки запрещён.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(Input.Password, user.PasswordHash))
            {
                ErrorMessage = "Неверный логин или пароль.";
                return Page();
            }

            var hasConnections = await _db.SupportUserConnections
                .AsNoTracking()
                .AnyAsync(c => c.UserId == user.Id);

            var isMarkedOnline = await _db.SupportUserSessions
                .AsNoTracking()
                .AnyAsync(s => s.UserId == user.Id && s.IsOnline);

            if (!hasConnections && isMarkedOnline)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions SET IsOnline = 0 WHERE UserId = {user.Id};");
                isMarkedOnline = false;
            }

            var alreadyOnline = hasConnections || isMarkedOnline;

            if (alreadyOnline && !Input.ForceLogin)
            {
                ErrorMessage = "Учётная запись уже активна в другой сессии. Включите «Войти принудительно».";
                return Page();
            }

            if (alreadyOnline && Input.ForceLogin)
            {
                var activeConnIds = await _db.SupportUserConnections
                    .AsNoTracking()
                    .Where(c => c.UserId == user.Id)
                    .Select(c => c.ConnectionId)
                    .ToListAsync();

                if (activeConnIds.Count > 0)
                {
                    await _hub.Clients.Clients(activeConnIds).SendAsync("ForceLogout");
                    _db.SupportUserConnections.RemoveRange(_db.SupportUserConnections.Where(c => c.UserId == user.Id));
                    await _db.SaveChangesAsync();
                }

                await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {DateTime.UtcNow} WHERE UserId = {user.Id};");
            }

            var now = DateTime.UtcNow;
            var jwt = Guid.NewGuid().ToString("N");

            var extraClaims = new List<Claim> { new("support_sid", jwt) };
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

            var updated = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE SupportUserSessions
SET IsOnline = 1, LastSeenUtc = {now}, JwtId = {jwt}
WHERE UserId = {user.Id};");

            if (updated == 0)
            {
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO SupportUserSessions(UserId, JwtId, IsOnline, LastSeenUtc)
VALUES ({user.Id}, {jwt}, 1, {now})
ON CONFLICT(UserId) DO UPDATE SET IsOnline = 1, LastSeenUtc = {now}, JwtId = {jwt};");
            }

            return LocalRedirect(Input.ReturnUrl ?? "/");
        }
    }
}
