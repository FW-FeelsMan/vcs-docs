using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace VCS_DOCs.Pages
{
	public class IndexModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<IndexModel> _logger;

		public User? CurrentUser { get; set; }

		public IndexModel(ILogger<IndexModel> logger, ApplicationDbContext context)
		{
			_context = context;
			_logger = logger;
		}

		public async Task<IActionResult> OnGetAsync()
		{
			// Если не аутентифицирован — сразу на логин
			if (User?.Identity?.IsAuthenticated != true)
				return RedirectToPage("/Login");

			// Берём userId из claim’ов
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return RedirectToPage("/Login");

			// Обновляем статус «онлайн»
			await UpdateUserStatus(userId, true);

			ViewData["Username"] = User.Identity.Name ?? "";

			// Ищем запись в БД по UserName
			CurrentUser = await _context.Users
				.FirstOrDefaultAsync(u => u.UserName == User.Identity.Name);

			// Если пользователя вдруг нет — отправляем на логин
			if (CurrentUser == null)
				return RedirectToPage("/Login");

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
