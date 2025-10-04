using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly IAntiforgery _antiforgery;
		private readonly UserDataPathOptions _options;
		private readonly UserStoragePaths _userPaths;
		public string AvatarPath { get; private set; } = "/images/default_avatar.png";
		public double UsedGb { get; private set; }
		public double FreeGb { get; private set; }
		public User? CurrentUser { get; private set; }
		private static readonly Regex ValidInputRegex = new(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);

		public profile_pageModel(
			ApplicationDbContext context,
			IAntiforgery antiforgery,
			IOptions<UserDataPathOptions> options
			,
			UserStoragePaths userPaths)
		{
			_context = context;
			_antiforgery = antiforgery;
			_options = options.Value;
			_userPaths = userPaths;
		}

		public async Task OnGetAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (!string.IsNullOrWhiteSpace(userId))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
				string shortUserId = userId.Replace("-", "").Substring(0, 8);
				string userDir = Path.Combine(_options.BasePath, $"u_{shortUserId}");

				long used = Directory.Exists(userDir)
					? Directory.GetFiles(userDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
					: 0;

				long free = 10L * 1024 * 1024 * 1024 - used;
				UsedGb = Math.Round(used / 1024.0 / 1024, 2);
				FreeGb = Math.Round(free / 1024.0 / 1024, 2);

				string avatarFolder = Path.Combine(userDir, "a");
				string avatarPath = Path.Combine(avatarFolder, "avatar.jpg");

				if (System.IO.File.Exists(avatarPath))
				{
					long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
					AvatarPath = $"/userdata/u_{shortUserId}/a/avatar.jpg?v={timestamp}";
				}
				else
				{
					AvatarPath = "/images/default_avatar.png";
				}
			}
		}
		/*public async Task<IActionResult> OnPostDeleteFileAsync(string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			string filePath = Path.Combine(_options.BasePath, $"userData_{userId}", fileName);
			if (!System.IO.File.Exists(filePath)) return new JsonResult(new { success = false, error = "Файл не найден" });

			try
			{
				System.IO.File.Delete(filePath);
				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}
		*/
		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			try { await _antiforgery.ValidateRequestAsync(HttpContext); }
			catch (AntiforgeryValidationException)
			{
				return new JsonResult(new { success = false, error = "Неверный токен безопасности" });
			}

			if (!User.Identity?.IsAuthenticated ?? true)
				return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });

			if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.Value) || request.Value.Length > 30)
				return new JsonResult(new { success = false, error = "Некорректные данные" });

			string? username = User.Identity?.Name;
			var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
			if (user == null) return new JsonResult(new { success = false, error = "Пользователь не найден" });

			switch (request.Field)
			{
				case "DateOfBirth":
					if (!DateTime.TryParseExact(request.Value!, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
						return new JsonResult(new { success = false, error = "Неверный формат даты" });
					user.DateOfBirth = parsedDate.ToString("dd.MM.yyyy");
					break;
				case "FullName":
					if (!ValidInputRegex.IsMatch(request.Value!)) return new JsonResult(new { success = false, error = "Недопустимые символы" });
					user.FullName = request.Value;
					break;
				case "Organization":
					if (!ValidInputRegex.IsMatch(request.Value!)) return new JsonResult(new { success = false, error = "Недопустимые символы" });
					user.Organization = request.Value;
					break;
				case "Department":
					if (!ValidInputRegex.IsMatch(request.Value!)) return new JsonResult(new { success = false, error = "Недопустимые символы" });
					user.Department = request.Value;
					break;
				case "Speciality":
					if (!ValidInputRegex.IsMatch(request.Value!)) return new JsonResult(new { success = false, error = "Недопустимые символы" });
					user.Speciality = request.Value;
					break;
				default:
					return new JsonResult(new { success = false, error = "Недопустимое поле" });
			}

			try
			{
				user.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();
				return new JsonResult(new { success = true });
			}
			catch (DbUpdateException ex)
			{
				return new JsonResult(new { success = false, error = ex.InnerException?.Message ?? ex.Message });
			}
		}

		public async Task<IActionResult> OnPostDeleteAccountAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
			if (user == null) return new JsonResult(new { success = false, error = "Пользователь не найден" });
			user.IsDeleted = true;
			user.UpdatedAt = DateTime.Now;
			try
			{
				await _context.SaveChangesAsync();
				await HttpContext.SignOutAsync();
				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}
		public async Task<IActionResult> OnPostUploadAvatarAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			var file = Request.Form.Files["avatar"];
			if (file == null || file.Length == 0)
				return new JsonResult(new { success = false, error = "Файл не получен" });

			var shortUserId = userId.Replace("-", "").Substring(0, 8);
			string avatarPath = _userPaths.GetAvatarPath(shortUserId);
			string avatarDir = Path.GetDirectoryName(avatarPath)!;
			Directory.CreateDirectory(avatarDir);

			string ext = Path.GetExtension(file.FileName).ToLower();
			if (ext != ".jpg" && ext != ".jpeg" && ext != ".png")
				return new JsonResult(new { success = false, error = "Неверный формат файла" });

			long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

			try
			{
				using var stream = new FileStream(avatarPath, FileMode.Create);
				await file.CopyToAsync(stream);
				return new JsonResult(new
				{
					success = true,
					userId = shortUserId,
					timestamp
				});
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}
	}

	public class UpdateUserRequest
	{
		public string? Field { get; set; }
		public string? Value { get; set; }
	}
}
