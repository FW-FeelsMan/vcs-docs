using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VCS_DOCs.Support.Pages.Support
{
    public class RequestModel : PageModel
    {
        private readonly IConfiguration _cfg;
        public RequestModel(IConfiguration cfg) => _cfg = cfg;

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
    }
}
