//<!--LoginSupport.cshtml.cs-->
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;

        public LoginModel(SignInManager<User> signInManager, UserManager<User> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ErrorMessage
        {
            get; set;
        }

        public class InputModel
        {
            public string Username { get; set; } = "";
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

        public async Task<IActionResult> OnPostAsync()
        {
            // Санитизация ReturnUrl
            if (string.IsNullOrEmpty(Input.ReturnUrl) ||
                !Url.IsLocalUrl(Input.ReturnUrl) ||
                Input.ReturnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase))
                Input.ReturnUrl = Url.Content("~/");

            var user = await _userManager.FindByNameAsync(Input.Username);
            if (user == null || user.IsDeleted || user.Access == 0)
            {
                ErrorMessage = "Неверное имя пользователя или аккаунт не активирован.";
                return Page();
            }

            if (!BCrypt.Net.BCrypt.Verify(Input.Password, user.PasswordHash))
            {
                ErrorMessage = "Неверный логин или пароль.";
                return Page();
            }

            await _signInManager.SignInAsync(user, isPersistent: true);

            var inSupport = await _userManager.IsInRoleAsync(user, Roles.SupportAgent) ||
                            await _userManager.IsInRoleAsync(user, Roles.SupportAdmin);

            if (!inSupport)
            {
                await _signInManager.SignOutAsync();
                return Forbid();
            }

            return LocalRedirect(Input.ReturnUrl ?? "/");
        }

        public void OnGet(string? returnUrl = null)
        {
            if (string.IsNullOrEmpty(returnUrl) ||
                !Url.IsLocalUrl(returnUrl) ||
                returnUrl.StartsWith("/Errors", StringComparison.OrdinalIgnoreCase))
                returnUrl = Url.Content("~/");

            Input.ReturnUrl = returnUrl;
        }
    }
}
