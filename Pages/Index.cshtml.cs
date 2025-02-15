using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Utilities;

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

		public IActionResult OnGet()
		{
			var user = HttpContext.Request.Cookies["AuthUser"];
			if (string.IsNullOrEmpty(user))
			{
				_logger.LogWarning("Пользователь не авторизован, перенаправление на страницу входа.");
				return RedirectToPage("/Login");
			}

			ViewData["Username"] = user;
			_logger.LogInformation($"Пользователь {user} зашел на главную страницу.");
			return Page();
		}

		[HttpPost]
		public async Task<IActionResult> OnPostSetUserOnlineAsync()
		{
			_logger.LogInformation("Метод OnPostSetUserOnlineAsync вызван.");
			var username = HttpContext.Request.Cookies["AuthUser"];

			if (string.IsNullOrEmpty(username))
				return Unauthorized();

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
			if (user == null)
				return NotFound();

			user.StatusOnline = 1;
			user.LastEntry = DateTime.UtcNow;
			await _context.SaveChangesAsync();

			return new JsonResult("OK");
		}

		[HttpPost]
		public async Task<IActionResult> OnPostSetUserOfflineAsync()
		{
			var username = HttpContext.Request.Cookies["AuthUser"];

			if (string.IsNullOrEmpty(username))
				return Unauthorized();

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
			if (user == null)
				return NotFound();

			user.StatusOnline = 0;
			await _context.SaveChangesAsync();

			return new JsonResult("OK");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> OnPostLogoutAsync()
		{
			Response.Cookies.Delete("AuthUser", new CookieOptions
			{
				Secure = true,
				SameSite = SameSiteMode.None,
				Path = "/"
			});

			Response.Cookies.Delete(".AspNetCore.Identity.Application");
			HttpContext.Session.Clear();
			await HttpContext.SignOutAsync("Identity.Application");

			return RedirectToPage("/Login");
		}
	}
}