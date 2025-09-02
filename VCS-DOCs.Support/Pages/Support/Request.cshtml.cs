using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VCS_DOCs.Support.Infrastructure.Provision;

namespace VCS_DOCs.Support.Pages.Support
{
    public class RequestModel : PageModel
    {
        private readonly IConfiguration _cfg;
        private readonly ISupportUserProvisioning _provisioning;
        private readonly ILogger<RequestModel> _log;

        public RequestModel(IConfiguration cfg, ISupportUserProvisioning provisioning, ILogger<RequestModel> log)
        {
            _cfg = cfg;
            _provisioning = provisioning;
            _log = log;
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

            [Required]
            [MinLength(3)]
            [MaxLength(200)]
            public string Subject { get; set; } = string.Empty;

            [Required]
            [MinLength(5)]
            [MaxLength(20_000)]
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
                    var errs = ModelState.Where(kv => kv.Value?.Errors.Count > 0)
                                         .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
                    return new JsonResult(new { success = false, error = "Некорректные данные", details = errs }) { StatusCode = 400 };
                }

                var fullName = (Input.FullName ?? "").Trim();
                var email = (Input.ReplyTo ?? "").Trim();
                var loginRaw = (Input.Login ?? "").Trim();

                if (string.IsNullOrWhiteSpace(loginRaw))
                {
                    if (!string.IsNullOrWhiteSpace(email) && email.Contains('@'))
                    {
                        loginRaw = email.Split('@')[0].Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(loginRaw))
                {
                    return new JsonResult(new { success = false, error = "Укажите логин или почту" }) { StatusCode = 400 };
                }

                var (user, created) = await _provisioning.EnsureUserExistsAsync(
                    login: loginRaw,
                    email: string.IsNullOrWhiteSpace(email) ? null : email,
                    fullName: string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                    organization: null,
                    department: null
                );

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
                return new JsonResult(new { success = false, error = "Внутренняя ошибка сервера" }) { StatusCode = 500 };
            }
        }
    }
}
