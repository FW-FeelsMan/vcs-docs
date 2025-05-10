using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using VCS_DOCs.Services.Upload;
using VCS_DOCs.Services.User;
using VCS_DOCs.Services;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Configuration;
using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly UserServiceManager _userServiceManager;
		private readonly FileUploadTaskService _taskService;
		private readonly IAntiforgery _antiforgery;
		private readonly IStorageQuotaService _quotaService;
		private readonly IHubContext<UserStorageHub> _hubContext;
		private readonly UserDataPathOptions _options;
		private const long MAX_CHUNK_SIZE = 2 * 1024 * 1024;
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
			UserServiceManager userServiceManager,
			FileUploadTaskService taskService,
			IAntiforgery antiforgery,
			IStorageQuotaService quotaService,
			IHubContext<UserStorageHub> hubContext,
			IOptions<UserDataPathOptions> options)
		{
			_context = context;
			_userServiceManager = userServiceManager;
			_taskService = taskService;
			_antiforgery = antiforgery;
			_quotaService = quotaService;
			_hubContext = hubContext;
			_options = options.Value;
		}

		public async Task OnGetAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			string? username = User.Identity?.Name;
			if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(username))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
				_userServiceManager.StartUserServices(userId, username);
				long used = await _quotaService.GetUsedBytesAsync(userId);
				long reserved = await _quotaService.GetReservedBytesAsync(userId);
				long free = 10L * 1024 * 1024 * 1024 - used - reserved;
				UsedGb = Math.Round(used / 1024.0 / 1024, 2);
				FreeGb = Math.Round(free / 1024.0 / 1024, 2);
			}
		}

		public class UpdateUserRequest
		{
			public string? Field { get; set; }
			public string? Value { get; set; }
		}
		public async Task<IActionResult> OnPostDeleteFileAsync(string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
			if (user == null)
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			string userFolder = Path.Combine(_options.BasePath, $"userData_{user.Id}"); // <-- тут поправка
			string filePath = Path.Combine(userFolder, fileName);

			if (!System.IO.File.Exists(filePath))
				return new JsonResult(new { success = false, error = "Файл не найден" });

			try
			{
				System.IO.File.Delete(filePath);

				var list = new List<object>();
				if (Directory.Exists(userFolder))
				{
					list = Directory
						.GetFiles(userFolder)
						.Select(f =>
						{
							var fi = new FileInfo(f);
							return new
							{
								name = fi.Name,
								sizeMb = Math.Round(fi.Length / 1048576.0, 2),
								lastWriteTime = fi.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
							};
						})
						.ToList<object>();
				}

				await _hubContext
					.Clients
					.Group(userId)
					.SendAsync("ReceiveStorageUpdate", list);

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}

		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			DateTime parsedDate = DateTime.MinValue;   // дефолтное значение

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

			string? username = User.Identity?.Name;
			if (string.IsNullOrWhiteSpace(username))
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == username);
			if (user == null)
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			switch (request.Field)
			{
				case "DateOfBirth":
					if (!DateTime.TryParseExact(
							request.Value!,
							"dd.MM.yyyy",
							CultureInfo.InvariantCulture,
							DateTimeStyles.None,
							out parsedDate))
					{
						return new JsonResult(new { success = false, error = "Неверный формат даты. Используйте ДД.MM.ГГГГ" });
					}
					user.DateOfBirth = parsedDate.ToString("dd.MM.yyyy");
					break;

				case "FullName":
					if (!ValidInputRegex.IsMatch(request.Value!))
						return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });
					user.FullName = request.Value;
					break;

				case "Organization":
					if (!ValidInputRegex.IsMatch(request.Value!))
						return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });
					user.Organization = request.Value;
					break;

				case "Department":
					if (!ValidInputRegex.IsMatch(request.Value!))
						return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });
					user.Department = request.Value;
					break;

				case "Speciality":
					if (!ValidInputRegex.IsMatch(request.Value!))
						return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });
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
				return new JsonResult(new { success = false, error = $"Ошибка базы данных: {ex.InnerException?.Message ?? ex.Message}" });
			}
		}

		public async Task<IActionResult> OnPostTryReserveAsync([FromForm] string fileName, [FromForm] long fileSize)
		{
			string? username = User.Identity?.Name;
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Имя файла не может быть пустым." });

			fileName = Path.GetFileName(fileName).Trim();
			string? extension = Path.GetExtension(fileName);

			if (BlockedExtensions.Contains(extension))
				return new JsonResult(new { success = false, error = "Загрузка исполняемых файлов запрещена." });

			if (fileName.Length > 120)
				return new JsonResult(new { success = false, error = "Имя файла слишком длинное (более 120 символов)." });

			char[] invalidChars = Path.GetInvalidFileNameChars();
			if (fileName.Any(c => invalidChars.Contains(c)))
				return new JsonResult(new { success = false, error = "Имя файла содержит недопустимые символы." });

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userId) || fileSize <= 0)
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			bool ok = await _quotaService.ReserveAsync(userId, fileName, fileSize);
			return new JsonResult(new { success = ok });
		}



		public class CancelTaskRequest
		{
			public string? TaskId { get; set; }
		}

		public async Task<IActionResult> OnGetStorageStatusAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false });

			long used = await _quotaService.GetUsedBytesAsync(userId);
			long reserved = await _quotaService.GetReservedBytesAsync(userId);
			long free = 10L * 1024 * 1024 * 1024 - used - reserved;

			return new JsonResult(new
			{
				success = true,
				usedMb = Math.Round(used / 1024.0 / 1024, 2),
				reservedMb = Math.Round(reserved / 1024.0 / 1024, 2),
				freeMb = Math.Round(free / 1024.0 / 1024, 2)
			});
		}
		public async Task<IActionResult> OnPostReleaseFileAsync(string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false });

			await _quotaService.ReleaseAsync(userId, fileName);
			return new JsonResult(new { success = true });
		}

		public async Task<IActionResult> OnPostUploadChunkAsync()
		{
			var headers = Request.Headers;

			if (!headers.ContainsKey("X-File-Name") ||
				!headers.ContainsKey("X-Chunk-Index") ||
				!headers.ContainsKey("X-Total-Chunks"))
			{
				return new JsonResult(new { success = false, error = "Отсутствуют необходимые заголовки." });
			}

			string? fileName = Uri.UnescapeDataString(headers["X-File-Name"]);
			if (!int.TryParse(headers["X-Chunk-Index"], out var chunkIndex) ||
				!int.TryParse(headers["X-Total-Chunks"], out var totalChunks) ||
				string.IsNullOrWhiteSpace(fileName))
			{
				return new JsonResult(new { success = false, error = "Неверные заголовки." });
			}

			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
				return new JsonResult(new { success = false, error = "Пользователь не авторизован" });

			string userFolder = Path.Combine(_options.BasePath, $"userData_{userId}");
			string chunkFolder = Path.Combine(userFolder, $"{fileName}_chunks");

			if (!Directory.Exists(chunkFolder))
				Directory.CreateDirectory(chunkFolder);

			try
			{
				if (chunkIndex == 0)
				{
					if (ActiveUploadsRegistry.IsActive(userId, fileName))
						return new JsonResult(new { success = false, error = "Файл уже загружается." });

					ActiveUploadsRegistry.Register(userId, fileName);
				}
				else
				{
					if (!ActiveUploadsRegistry.IsActive(userId, fileName))
						return new JsonResult(new { success = false, error = "Загрузка отменена пользователем" });
				}
				ActiveUploadsRegistry.Touch(userId, fileName);

				string chunkPath = Path.Combine(chunkFolder, $"chunk_{chunkIndex}");
				await using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
				{
					await Request.Body.CopyToAsync(fs);
				}

				long uploadedBytes = Directory
					.EnumerateFiles(chunkFolder, "chunk_*")
					.Sum(f => new FileInfo(f).Length);

				double totalApproxBytes = uploadedBytes + (totalChunks - chunkIndex - 1) * MAX_CHUNK_SIZE;

				await _hubContext.Clients.Group(userId).SendAsync("UploadProgress", new
				{
					name = fileName,
					uploadedBytes,
					totalBytes = totalApproxBytes
				});

				if (chunkIndex == totalChunks - 1)
				{
					string finalPath = Path.Combine(userFolder, fileName);
					await using (var dest = new FileStream(finalPath, FileMode.Create, FileAccess.Write))
					{
						for (int i = 0; i < totalChunks; i++)
						{
							string partPath = Path.Combine(chunkFolder, $"chunk_{i}");
							if (!System.IO.File.Exists(partPath))
								throw new IOException($"Чанк {i} не найден в {chunkFolder}");

							await using var src = new FileStream(partPath, FileMode.Open, FileAccess.Read);
							await src.CopyToAsync(dest);
						}
					}

					Directory.Delete(chunkFolder, recursive: true);

					// FIX: Снимаем резерв после успешной загрузки
					await _quotaService.ReleaseAsync(userId, fileName);

					var files = Directory
						.GetFiles(userFolder)
						.Select(f =>
						{
							var fi = new FileInfo(f);
							return new
							{
								name = fi.Name,
								sizeMb = Math.Round(fi.Length / 1048576.0, 2),
								lastWriteTime = fi.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
							};
						})
						.ToList();

					await _hubContext.Clients.Group(userId)
						.SendAsync("ReceiveStorageUpdate", files);

					ActiveUploadsRegistry.Unregister(userId, fileName);
				}

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				ActiveUploadsRegistry.Unregister(userId, fileName);
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}

		public async Task<JsonResult> OnGetFilesAsync()
		{
			if (User.Identity == null || !User.Identity.IsAuthenticated)
				return new JsonResult(new { success = false, message = "Not authenticated" });

			var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false, message = "User ID not found" });

			var files = await _context.FileReservations
				.Where(r => r.UserId == userId && !r.IsReleased)
				.Select(r => new
				{
					name = r.FileName,
					size = r.ReservedBytes
				})
				.ToListAsync();

			return new JsonResult(new { success = true, files });
		}
		public async Task<IActionResult> OnPostCancelUploadAsync([FromForm] string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false });

			ActiveUploadsRegistry.Unregister(userId, fileName);
			await _quotaService.ReleaseAsync(userId, fileName);

			string chunkFolder = Path.Combine(_options.BasePath, $"userData_{userId}", $"{fileName}_chunks");
			if (Directory.Exists(chunkFolder))
			{
				bool deleted = await SafeFileUtils.TryDeleteDirectoryWithRetries(chunkFolder);
			}

			await _hubContext.Clients.Group(userId)
				.SendAsync("UploadCancelled", fileName);

			return new JsonResult(new { success = true, fileName });
		}
		public async Task<IActionResult> OnGetIsFileUploadingAsync(string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(fileName))
				return new JsonResult(new { uploading = false });

			bool uploading = ActiveUploadsRegistry.IsActive(userId, fileName);
			return new JsonResult(new { uploading });
		}
		public async Task<IActionResult> OnPostDeleteAccountAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
			if (user == null)
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			user.IsDeleted = true;
			user.UpdatedAt = DateTime.UtcNow;

			try
			{
				await _context.SaveChangesAsync();
				await HttpContext.SignOutAsync(); 

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = $"Ошибка при удалении: {ex.Message}" });
			}
		}

	}
}
public static class SafeFileUtils
{
	public static async Task<bool> TryDeleteDirectoryWithRetries(string path, int retries = 5, int delayMs = 300)
	{
		if (!Directory.Exists(path))
			return true;

		for (int attempt = 1; attempt <= retries; attempt++)
		{
			try
			{
				Directory.Delete(path, recursive: true);
				return true;
			}
			catch (IOException)
			{
				// файл всё ещё занят
			}
			catch (UnauthorizedAccessException)
			{
				// Windows ещё держит файл
			}

			await Task.Delay(delayMs);
		}

		return false;
	}
}

