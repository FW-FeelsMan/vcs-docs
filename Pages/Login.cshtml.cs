using Microsoft.AspNetCore.Authentication;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VCS_DOCs.Utilities;

namespace VCS_DOCs.Pages
{
	public class LoginModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<LoginModel> _logger;
		public List<string> Specialities { get; set; }

		private readonly IWebHostEnvironment _webHostEnvironment;

		public LoginModel(ApplicationDbContext context, ILogger<LoginModel> logger, IWebHostEnvironment webHostEnvironment)
		{
			_context = context;
			_logger = logger;
			_webHostEnvironment = webHostEnvironment;
			LoginErrors = new List<string>();
			RegistrationErrors = new List<string>();
			Specialities = new List<string>();
		}

		[BindProperty]
		public string Username { get; set; }

		[BindProperty]
		public string Password { get; set; }
		public List<string> LoginErrors { get; set; }
		public List<string> RegistrationErrors { get; set; }
		public bool IsRegistrationSuccessful { get; set; }
		public string? ErrorMessage { get; set; }

		public async Task<IActionResult> OnPostLoginAsync()
		{
			if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
			{
				LoginErrors.Add("Имя пользователя и пароль обязательны.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}

			if (Username.Length > 20)
			{
				LoginErrors.Add("Имя пользователя не должно превышать 20 символов.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}

			if (!Regex.IsMatch(Username, @"^[a-zA-Z0-9]+$"))
			{
				LoginErrors.Add("Имя пользователя может содержать только латинские буквы и цифры.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}

			if (Password.Length > 20)
			{
				LoginErrors.Add("Пароль не должен превышать 20 символов.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}

			try
			{
				var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == Username);
				if (user == null || !BCrypt.Net.BCrypt.Verify(Password, user.Password))
				{
					LoginErrors.Add("Неверное имя пользователя или пароль.");
					return new JsonResult(new { success = false, errors = LoginErrors });
				}

				if (user.Access == 0)
				{
					LoginErrors.Add("Учетная запись не активирована.");
					return new JsonResult(new { success = false, errors = LoginErrors });
				}

				string? hardwareId = Request.Form["hardwareId"];
				if (!string.IsNullOrEmpty(hardwareId))
				{
					user.HardwareId = hardwareId;
				}

				user.LastEntry = DateTime.UtcNow;
				_context.Users.Update(user);
				await _context.SaveChangesAsync();

				var claims = new List<Claim>
				{
					new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
					new Claim(ClaimTypes.Name, user.Username)
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

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "An error occurred during login.");
				LoginErrors.Add("Произошла ошибка при входе в систему.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}
		}

		public async Task<IActionResult> OnPostRegisterAsync()
		{
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

			try
			{
				var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == Username);
				if (existingUser != null)
				{
					RegistrationErrors.Add("Пользователь с таким логином уже существует.");
					return new JsonResult(new { success = false, errors = RegistrationErrors });
				}

				string hashedPassword = BCrypt.Net.BCrypt.HashPassword(Password);

				var newUser = new User
				{
					Username = Username,
					Password = hashedPassword,
					Speciality = Request.Form["speciality"],
					StatusOnline = 0,
					HardwareId = null,
					LastEntry = null,
					CreatedAt = DateTime.Now,
					UpdatedAt = DateTime.Now,
					Access = 0
				};

				_context.Users.Add(newUser);
				await _context.SaveChangesAsync();

				string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
				string userDataPath = Path.Combine(appDataPath, $"userData_{Username}");
				
				if (!Directory.Exists(userDataPath))
				{
					Directory.CreateDirectory(userDataPath);
				}

				/*// Проверка размера папки пользователя
				long directorySize = GetDirectorySize(userDataPath);
				const long maxSize = 5L * 1024L * 1024L * 1024L; // 5 GB

				if (directorySize > maxSize)
				{
					RegistrationErrors.Add("Размер Вашей папки превышает лимит в 5 ГБ.");
					return new JsonResult(new { success = false, errors = RegistrationErrors });
				}
				*/
				IsRegistrationSuccessful = true;
				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка во время регистрации.");
				RegistrationErrors.Add("Произошла ошибка при регистрации.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}
		}

		private long GetDirectorySize(string directoryPath)
		{
			if (Directory.Exists(directoryPath))
			{
				return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
								.Sum(file => new FileInfo(file).Length);
			}
			return 0;
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
	}
}
