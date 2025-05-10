// Pages/Login.cshtml.cs
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Services.Microservices;
using VCS_DOCs.Services.User;
using VCS_DOCs.Utilities;

namespace VCS_DOCs.Pages
{
	public class LoginModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<LoginModel> _logger;
		private readonly ILogger<UserBackgroundService> _loggerUserBackgroundService;
		private readonly IServiceProvider _serviceProvider;
		private readonly IHubContext<UserStatusHub> _hubContext;
		private readonly IUserService _userService;
		private readonly IWebHostEnvironment _webHostEnvironment;

		public LoginModel(
			ApplicationDbContext context,
			ILogger<LoginModel> logger,
			ILogger<UserBackgroundService> userBackgroundServiceLogger,
			IServiceProvider serviceProvider,
			IHubContext<UserStatusHub> hubContext,
			IUserService userService,
			IWebHostEnvironment webHostEnvironment)
		{
			_context = context;
			_logger = logger;
			_loggerUserBackgroundService = userBackgroundServiceLogger;
			_serviceProvider = serviceProvider;
			_hubContext = hubContext;
			_userService = userService;
			_webHostEnvironment = webHostEnvironment;
			LoginErrors = new List<string>();
			RegistrationErrors = new List<string>();
			Specialities = new List<string>();
		}

		[BindProperty]
		public string Username { get; set; } = string.Empty;

		[BindProperty]
		public string Password { get; set; } = string.Empty;

		public List<string> LoginErrors { get; set; }
		public List<string> RegistrationErrors { get; set; }
		public bool IsRegistrationSuccessful { get; set; }
		public string? ErrorMessage { get; set; }
		public List<string> Specialities { get; set; }
		private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LastAttempt)> FailedLogins = new ConcurrentDictionary<string, (int, DateTime)>();

		public async Task<IActionResult> OnPostLoginAsync()
		{
			var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
			if (ip == null)
				return new JsonResult(new { success = false, errors = new List<string> { "Ошибка авторизации." } });

			if (FailedLogins.TryGetValue(ip, out var data))
			{
				if (data.Attempts >= 5 && (DateTime.UtcNow - data.LastAttempt).TotalMinutes < 10)
					return new JsonResult(new { success = false, errors = new List<string> { "Слишком много неудачных попыток. Попробуйте позже." } });
			}

			if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
			{
				FailedLogins[ip] = (data.Attempts + 1, DateTime.UtcNow);
				return new JsonResult(new { success = false, errors = new List<string> { "Имя пользователя и пароль обязательны." } });
			}

			var user = await _context.Users.FirstOrDefaultAsync(u => u.UserName == Username);

			if (user == null || user.IsDeleted || !BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
			{
				FailedLogins[ip] = (data.Attempts + 1, DateTime.UtcNow);
				return new JsonResult(new { success = false, errors = new List<string> { "Неверное имя пользователя, пароль или аккаунт был удалён." } });
			}

			if (user.Access == 0)
				return new JsonResult(new { success = false, errors = new List<string> { "Учетная запись не активирована." } });

			bool forceLogin = Request.Form["ForceLogin"].ToString().ToLower() == "true";

			if (user.StatusOnline == 1)
			{
				if (!forceLogin)
				{
					return new JsonResult(new
					{
						success = false,
						errors = new List<string> { "Этот аккаунт уже используется на другом устройстве." }
					});
				}
				await _hubContext.Clients.User(user.Id).SendAsync("ForceLogout");
				await _userService.ClearUserJwtIdAsync(user.Id);
				_context.Users.Update(user);
				await _context.SaveChangesAsync();
			}

			user.JwtId = Guid.NewGuid().ToString();
			user.HardwareId = Request.Form["hardwareId"].ToString() ?? user.HardwareId;
			user.LastEntry = DateTime.UtcNow;
			user.StatusOnline = 1;                       
			_context.Users.Update(user);
			await _context.SaveChangesAsync();

			var claims = new List<Claim>
			{
				new Claim(ClaimTypes.NameIdentifier, user.Id),
				new Claim(ClaimTypes.Name,           user.UserName),
				new Claim("JwtId",                   user.JwtId)
			};

			var authProperties = new AuthenticationProperties
			{
				IsPersistent = true,
				ExpiresUtc = DateTime.UtcNow.AddDays(7)
			};

			await HttpContext.SignInAsync(
				CookieAuthenticationDefaults.AuthenticationScheme,
				new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
				authProperties);

			FailedLogins.TryRemove(ip, out _);

			var userServiceManager = _serviceProvider.GetRequiredService<UserServiceManager>();
			userServiceManager.StartUserServices(user.Id, user.UserName);

			_logger.LogInformation($"Пользователь {user.UserName} вошел в систему.");

			return new JsonResult(new { success = true });
		}


		public async Task<IActionResult> OnPostRegisterAsync()
		{
			const int MaxPathLength = 260;

			if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
			{
				RegistrationErrors.Add("Имя пользователя и пароль обязательны.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			if (Username.Length > 20)
			{
				RegistrationErrors.Add("Имя пользователя не должно превышать 20 символов.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			if (!Regex.IsMatch(Username, @"^[a-zA-Z0-9]+$"))
			{
				RegistrationErrors.Add("Имя пользователя может содержать только латинские буквы и цифры.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			if (Password.Length > 20)
			{
				RegistrationErrors.Add("Пароль не должен превышать 20 символов.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			if (Password.Length < 6)
			{
				RegistrationErrors.Add("Пароль должен быть не менее 6 символов.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			try
			{
				var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.UserName == Username);
				if (existingUser != null)
				{
					RegistrationErrors.Add("Пользователь с таким логином уже существует.");
					return new JsonResult(new { success = false, errors = RegistrationErrors });
				}

				string hashedPassword = BCrypt.Net.BCrypt.HashPassword(Password);

				var newUser = new User
				{
					UserName = Username,
					PasswordHash = hashedPassword,
					Speciality = Request.Form["speciality"],
					StatusOnline = 0,
					HardwareId = null,
					LastEntry = null,
					CreatedAt = DateTime.Now,
					UpdatedAt = DateTime.Now,
					IsDeleted = false,
					Access = 0
				};

				_context.Users.Add(newUser);
				await _context.SaveChangesAsync();

				string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");

				string userFolderName = $"userData_{newUser.Id}";
				string userDataPath = Path.Combine(appDataPath, userFolderName);

				string historyFileName = $"history_{newUser.Id}.ini";
				string historyFilePath = Path.Combine(userDataPath, historyFileName);

				int fullFolderPathLength = Path.Combine(appDataPath, userFolderName).Length;
				int fullHistoryPathLength = Path.Combine(userDataPath, historyFileName).Length;

				if (fullFolderPathLength >= MaxPathLength || fullHistoryPathLength >= MaxPathLength)
				{
					_context.Users.Remove(newUser);
					await _context.SaveChangesAsync();

					RegistrationErrors.Add($"Не удалось создать пользователя: путь к папке или файлу слишком длинный ({fullHistoryPathLength} символов). Попробуйте использовать более короткий логин или другую базовую папку.");
					return new JsonResult(new { success = false, errors = RegistrationErrors });
				}

				if (!Directory.Exists(userDataPath))
				{
					Directory.CreateDirectory(userDataPath);
				}

				if (!System.IO.File.Exists(historyFilePath))
				{
					System.IO.File.WriteAllText(historyFilePath, "");
				}

				IsRegistrationSuccessful = true;

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				RegistrationErrors.Add("Произошла ошибка при регистрации.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}
		}

		public async Task<IActionResult> OnPostAsync(string action)
		{
			if (action == "Login")
			{
				return await OnPostLoginAsync();
			}
			else if (action == "Register")
			{
				return await OnPostRegisterAsync();
			}

			return Page();
		}

		public void OnGet()
		{
			var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Utilities", "Config.ini");
			Specialities = ConfigReader.GetSpecialities(configPath);
		}

		public void RecordDocumentHistory(string username, string documentName, string documentVersion)
		{
			string userDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData", $"userData_{username}");
			string historyFilePath = Path.Combine(userDataPath, $"history_{username}.ini");

			if (System.IO.File.Exists(historyFilePath))
			{
				var lines = System.IO.File.ReadAllLines(historyFilePath).ToList();

				var existingRecord = lines.FirstOrDefault(line => line.StartsWith(documentName));
				if (existingRecord != null)
				{
					lines[lines.IndexOf(existingRecord)] = $"{documentName}={documentVersion}";
				}
				else
				{
					lines.Add($"{documentName}={documentVersion}");
				}

				System.IO.File.WriteAllLines(historyFilePath, lines);
			}
			else
			{
				System.IO.File.WriteAllText(historyFilePath, $"{documentName}={documentVersion}");
			}
		}
	}
}
