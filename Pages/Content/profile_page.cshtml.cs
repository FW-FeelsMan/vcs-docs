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

			string userFolder = Path.Combine(_options.BasePath, $"userData_{user.UserName}");
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
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			fileName = Path.GetFileName(fileName);
			string extension = Path.GetExtension(fileName);

			if (BlockedExtensions.Contains(extension))
				return new JsonResult(new { success = false, error = "Загрузка исполняемых файлов запрещена." });

			if (fileName.Contains("..") || fileName.Contains('/') || fileName.Contains('\\'))

				return new JsonResult(new { success = false, error = "Имя файла содержит недопустимые символы." });

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userId) || fileSize <= 0 || string.IsNullOrWhiteSpace(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			bool ok = await _quotaService.ReserveAsync(userId, fileName, fileSize);
			return new JsonResult(new { success = ok });
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
			var form = Request.Form;
			var file = form.Files["chunk"];
			var fileName = Path.GetFileName(form["metadata.FileName"]);
			var chunkIndexStr = form["metadata.ChunkIndex"];
			var totalChunksStr = form["metadata.TotalChunks"];

			if (file == null || string.IsNullOrWhiteSpace(fileName) ||
				!int.TryParse(chunkIndexStr, out var chunkIndex) ||
				!int.TryParse(totalChunksStr, out var totalChunks))
			{
				return new JsonResult(new { success = false, error = "Неверные метаданные" });
			}

			string? username = User.Identity?.Name;
			string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userId))
				return new JsonResult(new { success = false, error = "Пользователь не авторизован" });

			string userFolder = Path.Combine(_options.BasePath, $"userData_{username}");
			string chunkFolder = Path.Combine(userFolder, $"{fileName}_chunks");

			if (!Directory.Exists(chunkFolder))
				Directory.CreateDirectory(chunkFolder);

			if (!_taskService.IsTaskActiveForFolder(chunkFolder))
			{
				var registrationTask = new FileUploadTask
				{
					UserId = userId,
					OriginalFileName = fileName,
					TempFilePath = chunkFolder,
					DestinationFolder = userFolder,
					TaskId = Guid.NewGuid().ToString()
				};
				_taskService.RegisterActiveTask(registrationTask);
			}

			string chunkPath = Path.Combine(chunkFolder, $"chunk_{chunkIndex}");
			using (var stream = new FileStream(chunkPath, FileMode.Create))
			{
				await file.CopyToAsync(stream);
			}

			if (chunkIndex == totalChunks - 1)
			{
				var finalTask = new FileUploadTask
				{
					UserId = userId,
					OriginalFileName = fileName,
					TempFilePath = chunkFolder,
					DestinationFolder = userFolder,
					TaskId = Guid.NewGuid().ToString()
				};
				_taskService.EnqueueTask(finalTask);				
			}

			return new JsonResult(new { success = true });
		}
	}
}
