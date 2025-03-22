using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VCS_DOCs.Data;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private static readonly Regex ValidInputRegex =	new Regex(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);

		public User CurrentUser { get; set; }

		public profile_pageModel(ApplicationDbContext context)
		{
			_context = context;
		}

		public async Task OnGetAsync()
		{
			string username = User.Identity?.Name;
			if (!string.IsNullOrEmpty(username))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
			}
		}

		public class UpdateUserRequest
		{
			public string Field { get; set; }
			public string Value { get; set; }
		}

		[ValidateAntiForgeryToken]
		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			if (!User.Identity.IsAuthenticated)
			{
				return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });
			}

			if (!ModelState.IsValid)
			{
				return new JsonResult(new { success = false, error = "Некорректная модель данных" });
			}

			if (string.IsNullOrWhiteSpace(request.Value))
			{
				return new JsonResult(new { success = false, error = "Поле не может быть пустым" });
			}
			if (request.Value.Length > 30)
			{
				return new JsonResult(new { success = false, error = "Длина значения не должна превышать 30 символов" });
			}
			if (!ValidInputRegex.IsMatch(request.Value))
			{
				return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });
			}

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
			if (user == null)
			{
				return new JsonResult(new { success = false, error = "Пользователь не найден" });
			}

			switch (request.Field)
			{
				case "FullName":
					user.FullName = request.Value;
					break;
				case "DateOfBirth":
					user.DateOfBirth = request.Value;
					break;
				case "Organization":
					user.Organization = request.Value;
					break;
				case "Department":
					user.Department = request.Value;
					break;
				case "Speciality":
					user.Speciality = request.Value;
					break;
				default:
					return new JsonResult(new { success = false, error = "Недопустимое поле для обновления" });
			}

			try
			{
				user.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();
				return new JsonResult(new { success = true });
			}
			catch (DbUpdateException ex)
			{
				Console.WriteLine($"Ошибка БД: {ex.InnerException?.Message}");
				return new JsonResult(new { success = false, error = $"Ошибка базы данных: {ex.InnerException?.Message}" });
			}
		}
	}
}
