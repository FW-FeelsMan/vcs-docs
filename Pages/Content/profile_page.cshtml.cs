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
		public string AvatarPath { get; private set; } = "/images/default_avatar.png";

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

			if (!string.IsNullOrWhiteSpace(userId))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
				_userServiceManager.StartUserServices(userId, CurrentUser?.UserName ?? "Unknown");

				long used = await _quotaService.GetUsedBytesAsync(userId);
				long reserved = await _quotaService.GetReservedBytesAsync(userId);
				long free = 10L * 1024 * 1024 * 1024 - used - reserved;

				UsedGb = Math.Round(used / 1024.0 / 1024, 2);
				FreeGb = Math.Round(free / 1024.0 / 1024, 2);

				Console.WriteLine($"CurrentUser: {CurrentUser?.UserName}, Id: {CurrentUser?.Id}");

				string avatarFolder = Path.Combine(_options.BasePath, $"userData_{userId}", "Avatars");
				if (Directory.Exists(avatarFolder))
				{
					var avatarFile = Directory.GetFiles(avatarFolder)
						.FirstOrDefault(f =>
							f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
							f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
							f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

					if (avatarFile != null)
					{
						var avatarFileName = Path.GetFileName(avatarFile);
						AvatarPath = $"/userdata/userData_{userId}/Avatars/{avatarFileName}";
					}
				}
			}
		}
		
		public async Task<IActionResult> OnPostDeleteFileAsync(string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
			if (user == null)
				return new JsonResult(new { success = false, error = "Пользователь не найден" });

			string userFolder = Path.Combine(_options.BasePath, $"userData_{user.Id}"); 
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
			DateTime parsedDate = DateTime.MinValue;  

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
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(fileName) || fileSize <= 0)
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			fileName = Path.GetFileName(fileName).Trim();

			if (BlockedExtensions.Contains(Path.GetExtension(fileName)))
				return new JsonResult(new { success = false, error = "Запрещённый тип файла" });

			if (Regex.IsMatch(fileName, @"\.v\d+\.0$", RegexOptions.IgnoreCase))
				return new JsonResult(new { success = false, error = "Имя файла не должно содержать .vX.0" });

			string finalFileName = await GenerateNextFileNameAsync(userId, fileName);
			bool success = await _quotaService.ReserveAsync(userId, finalFileName, fileSize);

			return new JsonResult(new { success, finalFileName = success ? finalFileName : null });
		}
		private async Task<string> GenerateNextFileNameAsync(string userId, string originalName)
		{
			string userFolder = Path.Combine(_options.BasePath, $"userData_{userId}");

			var existing = Directory.Exists(userFolder)
				? Directory.GetFiles(userFolder).Select(Path.GetFileName).Where(f => f != null).Cast<string>()
				: Enumerable.Empty<string>();

			var reserved = await _context.FileReservations
				.Where(r => r.UserId == userId)
				.Select(r => r.FileName)
				.ToListAsync();

			var all = new HashSet<string>(existing.Concat(reserved), StringComparer.OrdinalIgnoreCase);

			int version = 1;
			string candidate;
			do
			{
				candidate = $"{originalName}.v{version}.0";
				version++;
			} while (all.Contains(candidate));

			return candidate;
		}

		// В методе OnGetStorageStatusAsync в profile_page.cshtml.cs
		public async Task<IActionResult> OnGetStorageStatusAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false });

			long used = await _quotaService.GetUsedBytesAsync(userId);
			long reserved = await _quotaService.GetReservedBytesAsync(userId);
			long totalSpace = 10L * 1024 * 1024 * 1024; // 10 ГБ

			// Проверка на переполнение и коррекция значений
			if (used + reserved > totalSpace)
			{
				// Очистка устаревших резерваций
				await CleanupStaleReservationsAsync(userId);

				// Повторный запрос после очистки
				reserved = await _quotaService.GetReservedBytesAsync(userId);
			}

			// Гарантируем, что свободное место не будет отрицательным
			long free = Math.Max(0, totalSpace - used - reserved);

			return new JsonResult(new
			{
				success = true,
				usedMb = Math.Round(used / 1024.0 / 1024, 2),
				reservedMb = Math.Round(reserved / 1024.0 / 1024, 2),
				freeMb = Math.Round(free / 1024.0 / 1024, 2)
			});
		}

		// Добавить метод для очистки устаревших резерваций
		private async Task CleanupStaleReservationsAsync(string userId)
		{
			var staleReservations = await _context.FileReservations
				.Where(r => r.UserId == userId && r.CreatedAt < DateTime.UtcNow.AddHours(-24))
				.ToListAsync();

			if (staleReservations.Any())
			{
				_context.FileReservations.RemoveRange(staleReservations);
				await _context.SaveChangesAsync();
				Console.WriteLine($"Очищено {staleReservations.Count} устаревших резерваций для пользователя {userId}");
			}
		}

		public async Task<IActionResult> OnPostReleaseFileAsync([FromForm] string fileName)
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return new JsonResult(new { success = false });

			await _quotaService.ReleaseAsync(userId, fileName);

			var reservation = await _context.FileReservations.FirstOrDefaultAsync(r => r.UserId == userId && r.FileName == fileName);
			if (reservation != null)
			{
				_context.FileReservations.Remove(reservation);
				await _context.SaveChangesAsync();
			}

			return new JsonResult(new { success = true });
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
		public async Task<IActionResult> OnPostUploadChunkAsync()
		{
			var headers = Request.Headers;
			if (!headers.ContainsKey("X-File-Name") ||
				!headers.ContainsKey("X-Chunk-Index") ||
				!headers.ContainsKey("X-Total-Chunks"))
				return new JsonResult(new { success = false, error = "Отсутствуют необходимые заголовки." });

			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
				return new JsonResult(new { success = false, error = "Пользователь не авторизован" });

			string fileName = Uri.UnescapeDataString(headers["X-File-Name"]);
			if (!int.TryParse(headers["X-Chunk-Index"], out int chunkIndex) ||
				!int.TryParse(headers["X-Total-Chunks"], out int totalChunks) ||
				string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные заголовки." });

			// Проверка формата имени файла
			if (!Regex.IsMatch(fileName, @"\.v\d+\.0$", RegexOptions.IgnoreCase))
				return new JsonResult(new { success = false, error = "Имя файла должно содержать версионный постфикс .vX.0" });

			// Проверка резервирования
			bool reserved = await _context.FileReservations.AnyAsync(r => r.UserId == userId && r.FileName == fileName);
			if (!reserved)
				return new JsonResult(new { success = false, error = "Нет активной резервации" });

			// Определяем максимальный размер чанка в зависимости от общего размера файла
			long maxChunkSize = 10 * 1024 * 1024; // 10 МБ по умолчанию

			// Проверка размера чанка
			if (Request.ContentLength > maxChunkSize)
				return new JsonResult(new { success = false, error = "Чанк превышает допустимый размер." });

			string userFolder = Path.Combine(_options.BasePath, $"userData_{userId}");
			string chunkFolder = Path.Combine(userFolder, $"{fileName}_chunks");

			// Создаем структуру папок для группировки чанков
			// Для файлов > 1 ГБ используем подпапки для каждых 100 чанков
			string chunkPath;
			if (totalChunks > 500) // Примерно 5 ГБ при размере чанка 10 МБ
			{
				string subFolder = Path.Combine(chunkFolder, $"part_{chunkIndex / 100}");
				if (!Directory.Exists(subFolder))
					Directory.CreateDirectory(subFolder);
				chunkPath = Path.Combine(subFolder, $"chunk_{chunkIndex}");
			}
			else
			{
				if (!Directory.Exists(chunkFolder))
					Directory.CreateDirectory(chunkFolder);
				chunkPath = Path.Combine(chunkFolder, $"chunk_{chunkIndex}");
			}

			try
			{
				if (chunkIndex == 0)
				{
					if (ActiveUploadsRegistry.IsActive(userId, fileName))
						return new JsonResult(new { success = false, error = "Файл уже загружается." });

					ActiveUploadsRegistry.Register(userId, fileName);
				}
				else if (!ActiveUploadsRegistry.IsActive(userId, fileName))
				{
					return new JsonResult(new { success = false, error = "Загрузка не активна или истек таймаут." });
				}

				// Обновляем статус активности загрузки
				ActiveUploadsRegistry.Touch(userId, fileName);

				// Оптимизированная запись чанка с увеличенным буфером
				using (var fileStream = new FileStream(
					chunkPath,
					FileMode.Create,
					FileAccess.Write,
					FileShare.None,
					8192,
					FileOptions.Asynchronous | FileOptions.WriteThrough))
				{
					await Request.Body.CopyToAsync(fileStream);
				}

				// Если это последний чанк, запускаем сборку файла
				if (chunkIndex == totalChunks - 1)
				{
					var reservation = await _context.FileReservations
						.FirstOrDefaultAsync(r => r.UserId == userId && r.FileName == fileName);

					if (reservation != null)
					{
						var task = new FileUploadTask
						{
							UserId = userId,
							DestinationFolder = userFolder,
							TempFilePath = chunkFolder,
							OriginalFileName = fileName,
							FileLength = reservation.ReservedBytes
						};

						_taskService.EnqueueTask(task);
					}
				}

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}
		public async Task<IActionResult> OnPostTouchUploadAsync()
		{
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(userId))
				return new JsonResult(new { success = false });

			string fileName = Uri.UnescapeDataString(Request.Headers["X-File-Name"]);
			if (string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false });

			ActiveUploadsRegistry.Touch(userId, fileName);
			return new JsonResult(new { success = true });
		}

		public async Task<IActionResult> OnGetDownloadFileAsync(string fileName, string userId)
		{
			if (User.Identity == null || !User.Identity.IsAuthenticated)
				return Unauthorized();

			string? currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(currentUserId) || currentUserId != userId)
				return Forbid();

			if (string.IsNullOrWhiteSpace(fileName))
				return BadRequest("Неверное имя файла");

			var userFolder = Path.Combine(_options.BasePath, $"userData_{userId}");
			var filePath = Path.Combine(userFolder, fileName);

			if (!System.IO.File.Exists(filePath))
				return NotFound("Файл не найден");

			string downloadName = Regex.Replace(fileName, @"\.v\d+\.0$", "", RegexOptions.IgnoreCase);

			var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
			return File(fileStream, "application/octet-stream", downloadName);
		}

		private List<UserFileEntry> GetUserFiles(string userFolder)
		{
			var files = new DirectoryInfo(userFolder)
				.GetFiles()
				.Where(f => !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
							!f.Name.Contains("_chunks") &&
							!f.Name.EndsWith("_incomplete", StringComparison.OrdinalIgnoreCase))
				.ToList();
			Console.WriteLine("[server] Files from disk:");
			foreach (var f in files)
			{
				Console.WriteLine($"[server] {f.Name}");
			}
			var groups = files.GroupBy(file =>
			{
				// отделяем ".vX.0" если есть
				var match = Regex.Match(file.Name, @"^(.*)\.v\d+\.0$", RegexOptions.IgnoreCase);
				Console.WriteLine("match равен" ,match);
				var baseName = match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(file.Name);
				return baseName.ToLowerInvariant();
			});

			var entries = new List<UserFileEntry>();

			foreach (var group in groups)
			{
				var versions = group
					.Select(file =>
					{
						var verMatch = Regex.Match(file.Name, @"\.v(\d+)\.0$", RegexOptions.IgnoreCase);
						return verMatch.Success ? $"v{verMatch.Groups[1].Value}.0" : "v1.0";
					})
					.OrderBy(v => v)
					.ToList();

				var newestFile = group
					.OrderByDescending(f => f.LastWriteTimeUtc)
					.First();

				var displayName = Regex.Replace(newestFile.Name, @"\.\d$", "", RegexOptions.IgnoreCase);
				Console.WriteLine ("baseName равен ", displayName);
				var entry = new UserFileEntry
				{
					BaseName = group.Key,
					//Extension = Path.GetExtension(displayName), // фактическое расширение без версии
					Extension = Path.GetExtension(newestFile.Name), 
					DisplayName = Path.GetFileNameWithoutExtension(displayName),
					CurrentVersion = versions.LastOrDefault() ?? "v1.0",
					AllVersions = versions,
					SizeMb = Math.Round(newestFile.Length / 1048576.0, 2),
					LastWriteTime = newestFile.LastWriteTime.ToString("dd.MM.yyyy, HH:mm")
				};

				entries.Add(entry);
			}

			return entries;
		}
	}
}
public class CancelTaskRequest
{
	public string? TaskId { get; set; }
}
public class UpdateUserRequest
{
	public string? Field { get; set; }
	public string? Value { get; set; }
}
public class UserFileEntry
{
	public string BaseName { get; set; } = "";
	public string Extension { get; set; } = "";
	public string DisplayName { get; set; } = ""; 
	public string CurrentVersion { get; set; } = "v1.0";
	public List<string> AllVersions { get; set; } = new();
	public double SizeMb { get; set; }
	public string LastWriteTime { get; set; } = "";
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

