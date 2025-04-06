using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using VCS_DOCs.Hubs;
using VCS_DOCs.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly IWebHostEnvironment _webHostEnvironment;
		private readonly UserServiceManager _userServiceManager;
		private readonly FileUploadTaskService _taskService;
		private readonly IAntiforgery _antiforgery;

		private static readonly Regex ValidInputRegex = new(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);

		public User? CurrentUser { get; private set; }

		public profile_pageModel(ApplicationDbContext context,
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
			if (!string.IsNullOrWhiteSpace(username))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
				string userFolderPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData", $"userData_{username}");
				_userServiceManager.GetOrCreateStorageService(username, userFolderPath);
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
				await Task.Run(() => System.IO.File.Delete(filePath));
				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				return new JsonResult(new { success = false, error = ex.Message });
			}
		}

		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			try
			{
				await _antiforgery.ValidateRequestAsync(HttpContext);
			}
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

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
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

		public class ChunkMetadata
		{
			public string FileName { get; set; } = null!;
			public int ChunkIndex { get; set; }
			public int TotalChunks { get; set; }
		}

		public async Task<IActionResult> OnPostUploadChunkAsync([FromForm] IFormFile chunk, [FromForm] ChunkMetadata metadata)
		{
			string? username = User.Identity?.Name;
			string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(userId) || chunk == null)
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			string userFolderPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData", $"userData_{username}");
			string tempFolder = Path.Combine(userFolderPath, metadata.FileName + "_chunks");

			if (!Directory.Exists(tempFolder))
				Directory.CreateDirectory(tempFolder);

			string chunkPath = Path.Combine(tempFolder, $"chunk_{metadata.ChunkIndex:D6}.part");

			await using (var stream = new FileStream(chunkPath, FileMode.Create))
			{
				await chunk.CopyToAsync(stream);
			}

			if (metadata.ChunkIndex == metadata.TotalChunks - 1)
			{
				var task = new FileUploadTask
				{
					UserId = userId,
					DestinationFolder = userFolderPath,
					TempFilePath = tempFolder,
					OriginalFileName = metadata.FileName,
					FileLength = metadata.TotalChunks
				};
				_taskService.EnqueueTask(task);
			}

			double progress = ((double)(metadata.ChunkIndex + 1) / metadata.TotalChunks) * 100;
			var hubContext = HttpContext.RequestServices.GetRequiredService<IHubContext<UserStorageHub>>();
			await hubContext.Clients.Group(username).SendAsync("ReceiveUploadProgress", new { fileName = metadata.FileName, progress });

			return new JsonResult(new { success = true, progress });
		}
	}
}
