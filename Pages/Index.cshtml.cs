using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using VCS_DOCs.Configuration;

namespace VCS_DOCs.Pages
{
	public class IndexModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<IndexModel> _logger;
		public string UserStorageRootPath { get; private set; } = "";
		private readonly UserDataPathOptions _userDataOptions;

		public User? CurrentUser { get; set; }


		public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext context, IOptions<UserDataPathOptions> userDataOptions)
		{
			_context = context;
			_logger = logger;
			_userDataOptions = userDataOptions.Value;
		}

		public async Task<IActionResult> OnGetAsync()
		{		

			if (User?.Identity?.IsAuthenticated != true)
				return RedirectToPage("/Login");

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return RedirectToPage("/Login");

			await UpdateUserStatus(userId, true);
			ViewData["Username"] = User.Identity.Name ?? "";

			CurrentUser = await _context.Users
				.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);

			if (CurrentUser == null)
				return RedirectToPage("/Login");
			UserStorageRootPath = _userDataOptions.BasePath;
			ViewData["UserStorageBasePath"] = _userDataOptions.BasePath;

			return Page();
		}

		public async Task<IActionResult> OnPostLogoutAsync()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId))
				await UpdateUserStatus(userId, false);

			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToPage("/Login");
		}

		private async Task UpdateUserStatus(string? userId, bool isOnline)
		{
			if (string.IsNullOrEmpty(userId))
				return;

			// Теперь таблица AspNetUsers ключится по строковому UserId
			var user = await _context.Users.FindAsync(userId);
			if (user != null)
			{
				user.StatusOnline = isOnline ? 1 : 0;
				user.LastEntry = DateTime.UtcNow;
				await _context.SaveChangesAsync();
			}
		}
	}
}
