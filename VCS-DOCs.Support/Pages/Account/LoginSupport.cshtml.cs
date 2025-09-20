using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Buffers.Binary;
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
        private readonly ILogger<LoginSupportModel> _logger;

        private const int ACTIVE_TTL_SECONDS = 30;

        public LoginSupportModel(
            SignInManager<User> signInManager,
            UserManager<User> userManager,
            ApplicationDbContext db,
            IHubContext<SupportPresenceHub> hub,
            ILogger<LoginSupportModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _hub = hub;
            _logger = logger;
        }

        [BindProperty] public InputModel Input { get; set; } = new();
        public string? ErrorMessage
        {
            get; set;
        }

        public class InputModel
        {
            [Required(ErrorMessage = "Укажите логин.")]
            [StringLength(20, MinimumLength = 1, ErrorMessage = "Логин не более 20 символов.")]
            [RegularExpression("^[a-zA-Z0-9._-]+$", ErrorMessage = "Логин может содержать только латиницу, цифры, точку, подчёркивание и дефис.")]
            public string Username { get; set; } = "";

            [Required(ErrorMessage = "Укажите пароль.")]
            [StringLength(100, MinimumLength = 1, ErrorMessage = "Пароль не более 100 символов.")]
            public string Password { get; set; } = "";

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
            if (forced) Input.ReturnUrl = Url.Content("~/");
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
            // Триммим только логин; ПАРОЛЬ НЕ ТРОГАЕМ
            Input.Username = (Input.Username ?? string.Empty).Trim();
            Input.Password = Input.Password ?? string.Empty;

            // Санитация ReturnUrl
            if (string.IsNullOrEmpty(Input.ReturnUrl)
                || !Url.IsLocalUrl(Input.ReturnUrl)
                || Input.ReturnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase)
                || Input.ReturnUrl.StartsWith("/Account/LoginSupport", StringComparison.OrdinalIgnoreCase))
                Input.ReturnUrl = Url.Content("~/");

            // Серверная валидация через DataAnnotations
            if (!TryValidateModel(Input))
            {
                ErrorMessage = string.Join("; ",
                    ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage ?? e.Exception?.Message));
                return Page();
            }

            // Ищем пользователя ТАК ЖЕ, как в Web
            var norm = (Input.Username ?? string.Empty).ToUpperInvariant();
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.NormalizedUserName == norm);
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

            // (опционально) Инспекция хеша
            var meta = InspectIdentityV3(user.PasswordHash);
            if (meta != null)
            {
                _logger.LogInformation("SUPPORT LOGIN hash-inspect: user={user}, ver={ver}, prf={prf}, iter={it}, saltLen={salt}, subkeyLen={sk}",
                    user.UserName, meta.Value.Version, meta.Value.Prf, meta.Value.Iterations, meta.Value.SaltLength, meta.Value.SubkeyLength);
            }
            else
            {
                _logger.LogInformation("SUPPORT LOGIN hash-inspect: user={user}, format=Unknown, hashPrefix={pfx}",
                    user.UserName, (user.PasswordHash ?? string.Empty).Length >= 12 ? user.PasswordHash!.Substring(0, 12) : user.PasswordHash);
            }

            // Проверка пароля строго через Identity V3
            var v1 = PasswordVerificationResult.Failed;
            try { v1 = _userManager.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash ?? "", Input.Password); } catch { }
            var passwordOk = v1 is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

            _logger.LogInformation("SUPPORT LOGIN pwd-check: user={user}, pwdLen={len}, hashPrefix={pfx}, verify(UserMgr)={v1}",
                user.UserName,
                Input.Password?.Length ?? 0,
                (user.PasswordHash ?? string.Empty).Length >= 12 ? user.PasswordHash!.Substring(0, 12) : user.PasswordHash,
                v1);

            if (!passwordOk)
            {
                ErrorMessage = "Неверный логин или пароль.";
                return Page();
            }

            // Single-login
            var sess = await _db.SupportUserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == user.Id);
            var lastSeenUtc = sess?.LastSeenUtc ?? DateTime.MinValue;
            var isFresh = (DateTime.UtcNow - lastSeenUtc).TotalSeconds <= ACTIVE_TTL_SECONDS;
            var alreadyOnline = sess != null && !string.IsNullOrEmpty(sess.JwtId) && (sess.IsOnline || isFresh);

            if (alreadyOnline && !Input.ForceLogin)
            {
                ErrorMessage = "Учётная запись уже активна в другой сессии. Включите «Войти принудительно».";
                return Page();
            }

            var now = DateTime.UtcNow;
            var jwt = Guid.NewGuid().ToString("N");

            if (alreadyOnline && Input.ForceLogin)
            {
                try { await _hub.Clients.User(user.Id).SendAsync("ForceLogout", jwt); } catch { }
                await _db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE SupportUserSessions SET IsOnline = 0, LastSeenUtc = {now} WHERE UserId = {user.Id};");
            }

            var extraClaims = new List<Claim> { new("support_sid", jwt) };
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO SupportUserSessions(UserId, JwtId, IsOnline, LastSeenUtc)
                VALUES ({user.Id}, {jwt}, 1, {now})
                ON CONFLICT(UserId) DO UPDATE
                SET JwtId = {jwt}, IsOnline = 1, LastSeenUtc = {now};");

            return LocalRedirect(Input.ReturnUrl ?? "/");
        }

        // Корректный разбор V3 (big-endian ints)
        private static (byte Version, int Prf, int Iterations, int SaltLength, int SubkeyLength)? InspectIdentityV3(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return null;
            try
            {
                var bytes = Convert.FromBase64String(hash);
                if (bytes.Length < 17) return null;

                byte ver = bytes[0]; // 0x01 = V3, 0x00 = V2
                if (ver != 0x01 && ver != 0x00) return null;
                if (ver == 0x00) return (ver, -1, -1, -1, -1); // V2 без метаданных

                int prf = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(1));
                int iter = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(5));
                int saltLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(9));
                int skLen = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(13));

                return (ver, prf, iter, saltLen, skLen);
            }
            catch { return null; }
        }
    }
}