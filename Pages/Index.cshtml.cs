using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VCS_DOCs.Data;

namespace VCS_DOCs.Pages
{
	public class IndexModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<IndexModel> _logger;

		public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext context)
		{
			_context = context;
			_logger = logger;
		}

		public async Task<IActionResult> OnGet()
		{
			if (!User.Identity.IsAuthenticated)
			{
				return RedirectToPage("/Login");
			}

			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			await UpdateUserStatus(userId, true);

			ViewData["Username"] = User.Identity.Name;
			return Page();
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> OnPostLogoutAsync()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			await UpdateUserStatus(userId, false);

			await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
			return RedirectToPage("/Login");
		}

		private async Task UpdateUserStatus(string userId, bool isOnline)
		{
			if (!string.IsNullOrEmpty(userId) && int.TryParse(userId, out int id))
			{
				var user = await _context.Users.FindAsync(id);
				if (user != null)
				{
					user.StatusOnline = isOnline ? 1 : 0;
					user.LastEntry = DateTime.UtcNow;
					await _context.SaveChangesAsync();
				}
			}
		}
	}
}