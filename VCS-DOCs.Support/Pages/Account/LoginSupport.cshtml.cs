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

        private const int ACTIVE_TTL_SECONDS = 30; // окно "онлайна" для блокировки второго входа

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
        public void OnGet(string? returnUrl = null, string? message = null, bool forced = false)
        {
            if (forced)
            {
                Input.ReturnUrl = Url.Content("~/"); // после принудительного входа всегда на главную
            }
            else
            {
                if (string.IsNullOrEmpty(returnUrl)
                    || !Url.IsLocalUrl(returnUrl)
                    || returnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase)
                    || returnUrl.StartsWith("/Account/LoginSupport", StringComparison.OrdinalIgnoreCase))
                    returnUrl = Url.Content("~/");
                Input.ReturnUrl = returnUrl;
            }

            if (forced || string.Equals(message, "session_terminated", StringComparison.OrdinalIgnoreCase))
                ErrorMessage = "Вы отключены от сервера: выполнен принудительный вход с другого устройства";
        }

        public async Task<IActionResult> OnPostAsync()
        {
            // 1) Санитация ReturnUrl (не даём возвращаться на /Account/LoginSupport и ошибки)
            if (string.IsNullOrEmpty(Input.ReturnUrl)
                || !Url.IsLocalUrl(Input.ReturnUrl)
                || Input.ReturnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase)
                || Input.ReturnUrl.StartsWith("/Account/LoginSupport", StringComparison.OrdinalIgnoreCase))
            {
                Input.ReturnUrl = Url.Content("~/");
            }

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

            // 2) Проверка «уже онлайн»
            var sess = await _db.SupportUserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == user.Id);
            var lastSeenUtc = sess?.LastSeenUtc ?? DateTime.MinValue;
            var isFresh = (DateTime.UtcNow - lastSeenUtc).TotalSeconds <= ACTIVE_TTL_SECONDS;

            // активной считаем запись, у которой есть JwtId И (IsOnline==true или пульс свежий)
            var alreadyOnline = sess != null
                                && !string.IsNullOrEmpty(sess.JwtId)
                                && (sess.IsOnline || isFresh);

            if (alreadyOnline && !Input.ForceLogin)
            {
                ErrorMessage = "Учётная запись уже активна в другой сессии. Включите «Войти принудительно».";
                return Page();
            }

            // 3) Готовим новый sid и, если надо, мягко кикаем старые вкладки
            var now = DateTime.UtcNow;
            var jwt = Guid.NewGuid().ToString("N");

            if (alreadyOnline && Input.ForceLogin)
            {
                try
                {
                    // важное: передаём НОВЫЙ sid — новая вкладка проигнорирует этот ForceLogout
                    await _hub.Clients.User(user.Id).SendAsync("ForceLogout", jwt);
                }
                catch { /* best-effort */ }

                // не чистим JwtId здесь — просто сбрасываем флаг онлайна;
                // middleware дожмёт старые вкладки, если не отреагировали на SignalR.
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE SupportUserSessions
                SET IsOnline = 0, LastSeenUtc = {now}
                WHERE UserId = {user.Id};");
            }

            // 4) Логин с НОВЫМ sid в куку
            var extraClaims = new List<Claim> { new("support_sid", jwt) };
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

            // 5) Апсертим ту же сессию в БД
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO SupportUserSessions(UserId, JwtId, IsOnline, LastSeenUtc)
            VALUES ({user.Id}, {jwt}, 1, {now})
            ON CONFLICT(UserId) DO UPDATE
            SET JwtId = {jwt}, IsOnline = 1, LastSeenUtc = {now};");

            // 6) Готово
            return LocalRedirect(Input.ReturnUrl ?? "/");
        }
    }
}
