using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using VCS_DOCs.Configuration;
using VCS_DOCs.Data;

using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Pages
{
	public class IndexModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<IndexModel> _logger;
		public string UserStorageRootPath { get; private set; } = "";
		private readonly UserDataPathOptions _userDataOptions;
		private readonly IWebHostEnvironment _env;
		public User? CurrentUser { get; set; }
		public string AvatarUrl { get; set; } = "";

		public IndexModel(
		ILogger<IndexModel> logger,
		ApplicationDbContext context,
		IOptions<UserDataPathOptions> userDataOptions,
		IWebHostEnvironment env)
		{
			_context = context;
			_logger = logger;
			_userDataOptions = userDataOptions.Value;
			_env = env;
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

			var shortId = CurrentUser.Id.Replace("-", "").Substring(0, 8);
			var avatarPath = Path.Combine(_userDataOptions.BasePath, $"u_{shortId}", "a", "avatar.jpg");

			if (System.IO.File.Exists(avatarPath))
			{
				AvatarUrl = $"/userdata/u_{shortId}/a/avatar.jpg?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
			}
			else
			{
				AvatarUrl = "/images/default_avatar.png";
			}

			//_logger.LogInformation("Проверяем путь к аватарке: {Path}", avatarPath);
			//_logger.LogInformation("Файл существует? {Exists}", System.IO.File.Exists(avatarPath));

			if (System.IO.File.Exists(avatarPath))
			{
				AvatarUrl = $"/userdata/u_{shortId}/a/avatar.jpg?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
			}
			else
			{
				AvatarUrl = "/images/default_avatar.png";
			}

			return Page();
		}

		public async Task<IActionResult> OnPostLogoutAsync()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrEmpty(userId))
				await UpdateUserStatus(userId, false);

			await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
			return RedirectToPage("/Login");
		}

		private async Task UpdateUserStatus(string? userId, bool isOnline)
		{
			if (string.IsNullOrEmpty(userId))
				return;

			var user = await _context.Users.FindAsync(userId);
			if (user != null)
			{
				user.StatusOnline = isOnline ? 1 : 0;
				user.LastEntry = DateTime.Now;
				await _context.SaveChangesAsync();
			}
		}
	}
}
