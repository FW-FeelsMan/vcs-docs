using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VCS_DOCs.Data;
using VCS_DOCs.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace VCS_DOCs.Pages.Content
{
	public class profile_pageModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly IWebHostEnvironment _webHostEnvironment;
		private readonly UserServiceManager _userServiceManager;
		private readonly UserFileUploadService _uploadService;
		private readonly FileUploadTaskService _taskService;
		private readonly IAntiforgery _antiforgery;

		private static readonly Regex ValidInputRegex = new Regex(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);

		public User CurrentUser { get; set; }

		[BindProperty(SupportsGet = false, Name = "UploadFile")]
		public IFormFile? UploadFile { get; set; }

		public profile_pageModel(ApplicationDbContext context,
								 IWebHostEnvironment webHostEnvironment,
								 UserServiceManager userServiceManager,
								 UserFileUploadService uploadService,
								 FileUploadTaskService taskService,
								 IAntiforgery antiforgery)
		{
			_context = context;
			_webHostEnvironment = webHostEnvironment;
			_userServiceManager = userServiceManager;
			_uploadService = uploadService;
			_taskService = taskService;
			_antiforgery = antiforgery;
		}

		public async Task OnGetAsync()
		{
			string username = User.Identity?.Name;
			if (!string.IsNullOrEmpty(username))
			{
				CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
				string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
				string userFolderPath = Path.Combine(appDataPath, $"userData_{username}");
				_userServiceManager.GetOrCreateStorageService(username, userFolderPath);
			}
		}

		public async Task<IActionResult> OnPostUploadFileAsync()
		{
			string username = User.Identity?.Name;
			if (string.IsNullOrEmpty(username) || UploadFile == null)
				return new JsonResult(new { success = false, error = "Файл не выбран" });

			string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
			string userFolderPath = Path.Combine(appDataPath, $"userData_{username}");
			if (!Directory.Exists(userFolderPath))
				Directory.CreateDirectory(userFolderPath);

			string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.GetExtension(UploadFile.FileName));
			using (var stream = new FileStream(tempFile, FileMode.Create))
				await UploadFile.CopyToAsync(stream);

			var fileTask = new FileUploadTask
			{
				UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
				DestinationFolder = userFolderPath,
				TempFilePath = tempFile,
				OriginalFileName = UploadFile.FileName,
				FileLength = UploadFile.Length
			};

			_taskService.EnqueueTask(fileTask);
			return new JsonResult(new { success = true });
		}

		public async Task<IActionResult> OnPostDeleteFileAsync(string fileName)
		{
			string username = User.Identity?.Name;
			if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(fileName))
				return new JsonResult(new { success = false, error = "Неверные параметры" });

			string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
			string userFolderPath = Path.Combine(appDataPath, $"userData_{username}");
			string filePath = Path.Combine(userFolderPath, fileName);

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

		public class UpdateUserRequest
		{
			public string Field { get; set; }
			public string Value { get; set; }
		}

		public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request)
		{
			var antiforgeryToken = HttpContext.Request.Headers["X-CSRF-TOKEN"].FirstOrDefault();
			try
			{
				await _antiforgery.ValidateRequestAsync(HttpContext);
			}
			catch (AntiforgeryValidationException)
			{
				return new JsonResult(new { success = false, error = "Неверный токен безопасности" });
			}

			if (!User.Identity.IsAuthenticated)
				return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });
			if (!ModelState.IsValid)
			{
				var allErrors = ModelState
					.Where(ms => ms.Value.Errors.Count > 0)
					.SelectMany(ms => ms.Value.Errors)
					.Select(e => e.ErrorMessage)
					.ToList();

				var exceptionErrors = ModelState
					.Where(ms => ms.Value.Errors.Count > 0)
					.SelectMany(ms => ms.Value.Errors)
					.Select(e => e.Exception?.Message)
					.Where(msg => msg != null)
					.ToList();

				// тут смотри переменные allErrors и exceptionErrors
				return new JsonResult(new
				{
					success = false,
					error = "Некорректная модель данных",
					details = allErrors,
					exceptions = exceptionErrors
				});
			}


			if (string.IsNullOrWhiteSpace(request.Value))
				return new JsonResult(new { success = false, error = "Поле не может быть пустым" });

			if (request.Value.Length > 30)
				return new JsonResult(new { success = false, error = "Длина значения не должна превышать 30 символов" });

			if (!ValidInputRegex.IsMatch(request.Value))
				return new JsonResult(new { success = false, error = "Значение содержит недопустимые символы" });

			var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
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
				return new JsonResult(new { success = false, error = $"Ошибка базы данных: {ex.InnerException?.Message}" });
			}
		}
	}
}
