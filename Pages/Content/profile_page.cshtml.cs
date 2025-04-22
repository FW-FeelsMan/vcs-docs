using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VCS_DOCs.Services;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly IWebHostEnvironment _webHostEnvironment;
		private readonly UserServiceManager _userServiceManager;
		private readonly FileUploadTaskService _taskService;
		private readonly IAntiforgery _antiforgery;

		public double UsedGb { get; private set; }
		public double FreeGb { get; private set; }
		public User? CurrentUser { get; private set; }

		private static readonly Regex ValidInputRegex = new(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);
		private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".exe", ".bat", ".cmd", ".sh", ".msi", ".dll", ".js", ".jar", ".vbs", ".ps1", ".scr", ".php", ".py", ".rb",
			".com", ".cpl", ".gadget", ".msu", ".reg", ".vb", ".wsf", ".pif", ".app", ".apk", ".hta", ".pl", ".cgi"
		};

		public profile_pageModel(
			ApplicationDbContext context,
			IWebHostEnvironment webHostEnvironment,
			UserServiceManager userServiceManager,
			FileUploadTaskService taskService,
			IAntiforgery antiforgery)
		{
			_context = context;
			_webHostEnvironment = webHostEnvironment;
			_userServiceManager = userServiceManager;
			_taskService = taskService;
			_antiforgery = antiforgery;
		}

		public async Task OnGetAsync()
		{
			string? username = User.Identity?.Name;
			string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

			if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(userId))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
				_userServiceManager.StartUserServices(userId, username);

				string userFolder = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData", $"userData_{username}");
				long usedBytes = Directory.Exists(userFolder)
					? Directory.GetFiles(userFolder).Sum(f => new FileInfo(f).Length)
					: 0;

				UsedGb = Math.Round((double)usedBytes / 1024 / 1024 / 1024, 2);
				FreeGb = Math.Round((10L * 1024 * 1024 * 1024 - usedBytes) / 1024.0 / 1024 / 1024, 2);
			}
		}

		public class UpdateUserRequest
		{
			public string? Field { get; set; }
			public string? Value { get; set; }
		}

		public async Task<IActionResult> OnPostDeleteFileAsync(string fileName)
		{
			string? username = User.Identity?.Name;
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			string filePath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData", $"userData_{username}", fileName);

			if (!System.IO.File.Exists(filePath))
				return new JsonResult(new { success = false, error = "Файл не найден" });

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

		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			try { await _antiforgery.ValidateRequestAsync(HttpContext); }
			catch (AntiforgeryValidationException)
			{
				return new JsonResult(new { success = false, error = "Неверный токен безопасности" });
			}

			if (User.Identity?.IsAuthenticated != true)
				return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });

			if (!ModelState.IsValid)
			{
				var allErrors = ModelState
					.SelectMany(ms => ms.Value?.Errors ?? Enumerable.Empty<ModelError>())
					.Select(e => e.ErrorMessage)
					.ToList();

				return new JsonResult(new { success = false, error = "Некорректная модель данных", details = allErrors });
			}

			if (string.IsNullOrWhiteSpace(request.Value))
				return new JsonResult(new { success = false, error = "Поле не может быть пустым" });

			if (request.Value.Length > 30)
				return new JsonResult(new { success = false, error = "Длина значения не должна превышать 30 символов" });

			if (!ValidInputRegex.IsMatch(request.Value))
				return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });

			string? username = User.Identity?.Name;
			if (string.IsNullOrWhiteSpace(username))
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
			if (user == null)
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			switch (request.Field)
			{
				case "FullName": user.FullName = request.Value; break;
				case "DateOfBirth": user.DateOfBirth = request.Value; break;
				case "Organization": user.Organization = request.Value; break;
				case "Department": user.Department = request.Value; break;
				case "Speciality": user.Speciality = request.Value; break;
				default: return new JsonResult(new { success = false, error = "Недопустимое поле для обновления" });
			}

			try
			{
				user.UpdatedAt = DateTime.Now;
				await _context.SaveChangesAsync();
				return new JsonResult(new { success = true });
			}
			catch (DbUpdateException ex)
			{
				return new JsonResult(new { success = false, error = $"Ошибка базы данных: {ex.InnerException?.Message ?? ex.Message}" });
			}
		}

		public async Task<IActionResult> OnPostTryReserveAsync([FromForm] string fileName, [FromForm] long fileSize)
		{
			string? username = User.Identity?.Name;
			string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

			fileName = Path.GetFileName(fileName);
			string extension = Path.GetExtension(fileName);

			if (BlockedExtensions.Contains(extension))
				return new JsonResult(new { success = false, error = "Загрузка исполняемых файлов запрещена." });

			if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))
				return new JsonResult(new { success = false, error = "Имя файла содержит недопустимые символы." });

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userId) || fileSize <= 0 || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			// Здесь позже будет запись в INI: fileName=fileSize
			return new JsonResult(new { success = true });
		}

		public IActionResult OnPostCancelUpload([FromBody] CancelTaskRequest request)
		{
			if (string.IsNullOrWhiteSpace(request.TaskId))
				return new JsonResult(new { success = false });

			bool cancelled = _taskService.CancelTask(request.TaskId);
			return new JsonResult(new { success = cancelled });
		}

		public class CancelTaskRequest
		{
			public string? TaskId { get; set; }
		}
	}
}
