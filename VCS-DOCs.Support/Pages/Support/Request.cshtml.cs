using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Support.Infrastructure.Provision;

namespace VCS_DOCs.Support.Pages.Support
{
    public class RequestModel : PageModel
    {
        private readonly IConfiguration _cfg;
        private readonly ISupportUserProvisioning _provisioning;
        private readonly ILogger<RequestModel> _log;
        private readonly ApplicationDbContext _db;

        private readonly long _defaultStorageLimitBytes;

        public RequestModel(
            IConfiguration cfg,
            ISupportUserProvisioning provisioning,
            ILogger<RequestModel> log,
            ApplicationDbContext db)
        {
            _cfg = cfg;
            _provisioning = provisioning;
            _log = log;
            _db = db;

            _defaultStorageLimitBytes =
                _cfg.GetValue<long?>("Storage:DefaultLimitBytes")
                ?? 10L * 1024 * 1024 * 1024;
        }

        public bool CaptchaEnabled
        {
            get; private set;
        }
        public string CaptchaProvider { get; private set; } = "ReCaptchaV2";
        public string? CaptchaSiteKey
        {
            get; private set;
        }

        public void OnGet()
        {
            CaptchaEnabled = _cfg.GetValue<bool>("Captcha:Enabled");
            CaptchaProvider = _cfg["Captcha:Provider"] ?? "ReCaptchaV2";
            CaptchaSiteKey = _cfg["Captcha:SiteKey"];
        }

        public sealed class InputModel
        {
            public string? FullName
            {
                get; set;
            }
            public string? Login
            {
                get; set;
            }

            [EmailAddress]
            public string? ReplyTo
            {
                get; set;
            }

            [Required, MinLength(3), MaxLength(200)]
            public string Subject { get; set; } = string.Empty;

            [Required, MinLength(5), MaxLength(20_000)]
            public string Message { get; set; } = string.Empty;

            public string? CaptchaAnswer
            {
                get; set;
            }
            public string? CaptchaToken
            {
                get; set;
            }
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        [ValidateAntiForgeryToken]
        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errs = ModelState
                        .Where(kv => kv.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kv => kv.Key,
                            kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                        );

                    return new JsonResult(new
                    {
                        success = false,
                        error = "Некорректные данные",
                        details = errs
                    })
                    {
                        StatusCode = 400
                    };
                }

                var fullName = (Input.FullName ?? "").Trim();
                var email = (Input.ReplyTo ?? "").Trim();
                var loginRaw = (Input.Login ?? "").Trim();

                // автогенерация логина из email при необходимости
                if (string.IsNullOrWhiteSpace(loginRaw))
                {
                    if (!string.IsNullOrWhiteSpace(email) && email.Contains('@'))
                    {
                        var (local, domainRoot) = SplitEmail(email);
                        var baseLogin = SanitizeLoginBase(local);
                        if (string.IsNullOrWhiteSpace(baseLogin))
                            baseLogin = "user";

                        loginRaw = await BuildUniqueLoginAsync(baseLogin, domainRoot);
                    }
                }

                if (string.IsNullOrWhiteSpace(loginRaw))
                {
                    return new JsonResult(new
                    {
                        success = false,
                        error = "Укажите логин или почту"
                    })
                    {
                        StatusCode = 400
                    };
                }

                _log.LogInformation("PROVISION start: login={Login} email={Email}", loginRaw, email);

                var (user, created) = await _provisioning.EnsureUserExistsAsync(
                    login: loginRaw,
                    email: string.IsNullOrWhiteSpace(email) ? null : email,
                    fullName: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                    organization: null,
                    department: null
                );

                _log.LogInformation("PROVISION done: id={Id} login={Login} created={Created}",
                    user.Id, user.UserName, created);

                // NEW: гарантируем установку StorageLimitBytes
                var dbUser = await _db.Users.FirstOrDefaultAsync(u => u.Id == user.Id);
                if (dbUser != null && (dbUser.StorageLimitBytes == null || dbUser.StorageLimitBytes <= 0))
                {
                    dbUser.StorageLimitBytes = _defaultStorageLimitBytes;
                    await _db.SaveChangesAsync();
                    _log.LogInformation("Set StorageLimitBytes={Limit} for user {Id}", dbUser.StorageLimitBytes, dbUser.Id);
                }

                var traceId = HttpContext.TraceIdentifier;

                return new JsonResult(new
                {
                    success = true,
                    created,
                    userId = user.Id,
                    login = user.UserName,
                    email = user.Email,
                    traceId
                });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Support request submission failed");
                return new JsonResult(new
                {
                    success = false,
                    error = "Внутренняя ошибка сервера"
                })
                {
                    StatusCode = 500
                };
            }
        }

       
        private static (string local, string? domainRoot) SplitEmail(string email)
        {
            try
            {
                var parts = email.Split('@', 2);
                var local = parts[0];
                string? domainRoot = null;
                if (parts.Length == 2)
                {
                    var dom = parts[1];
                    var dot = dom.IndexOf('.');
                    domainRoot = dot > 0 ? dom[..dot] : dom;
                }
                return (local, domainRoot);
            }
            catch { return (email, null); }
        }

        private static string SanitizeLoginBase(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = Regex.Replace(raw, @"[^a-zA-Z0-9._-]+", "").ToLowerInvariant();
            s = Regex.Replace(s, @"([._-])\1{1,}", "$1").Trim('.', '_', '-');
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            if (!char.IsLetterOrDigit(s[0])) s = "u-" + s;
            return s;
        }

        private async Task<string> BuildUniqueLoginAsync(string baseLogin, string? domainRoot, int maxLen = 32)
        {
            baseLogin = TruncateClean(baseLogin, maxLen);
            var candidates = new List<string> { baseLogin };

            if (!string.IsNullOrWhiteSpace(domainRoot))
            {
                var dom = SanitizeLoginBase(domainRoot);
                if (!string.IsNullOrWhiteSpace(dom))
                    candidates.Add(WithSuffix(baseLogin, dom, maxLen)); 
            }

            foreach (var cand in candidates)
                if (!await ExistsUserNameAsync(cand)) return cand;

            var seed = candidates.Last();
            for (int i = 1; i <= 1000; i++)
            {
                var cand = WithSuffix(seed, i.ToString(), maxLen);
                if (!await ExistsUserNameAsync(cand)) return cand;
            }

            var tail = Convert.ToString(Random.Shared.NextInt64(), 36)![..6];
            return WithSuffix(baseLogin, tail, maxLen);
        }

        private static string TruncateClean(string s, int maxLen)
            => string.IsNullOrEmpty(s) || s.Length <= maxLen ? s : s[..maxLen];

        private static string WithSuffix(string basePart, string suffix, int maxLen)
        {
            const string sep = "-";
            var need = suffix.Length + sep.Length;
            var head = basePart;
            if (head.Length + need > maxLen)
            {
                head = head[..Math.Max(1, maxLen - need)];
                head = head.Trim('.', '_', '-');
            }
            var res = $"{head}{sep}{suffix}".Trim('.', '_', '-');
            return TruncateClean(res, maxLen);
        }

        private async Task<bool> ExistsUserNameAsync(string candidate)
        {
            var norm = candidate.ToUpperInvariant();
            return await _db.Users.AsNoTracking().AnyAsync(u => u.NormalizedUserName == norm);
        }
    }
}
