using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VCS_DOCs.Utilities;
using VCS_DOCs.Data;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;
using VCS_DOCs.Services;

namespace VCS_DOCs.Pages
{
	public class LoginModel : PageModel
	{
		private readonly ApplicationDbContext _context;
		private readonly ILogger<LoginModel> _logger;
		private readonly IWebHostEnvironment _webHostEnvironment;
		private readonly IHubContext<UserStatusHub> _hubContext;
		private readonly IUserService _userService;
		public LoginModel(
		ApplicationDbContext context,
		ILogger<LoginModel> logger,
		IWebHostEnvironment webHostEnvironment,
		IHubContext<UserStatusHub> hubContext,
		IUserService userService) 
		{
			_context = context;
			_logger = logger;
			_webHostEnvironment = webHostEnvironment;
			_hubContext = hubContext;
			_userService = userService; 
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
		public List<string> Specialities { get; set; }

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

				bool forceLogin = Request.Form["ForceLogin"].ToString().ToLower() == "true";

				if (!string.IsNullOrEmpty(user.JwtId))
				{
					if (!forceLogin)
					{
						LoginErrors.Add("Этот аккаунт уже используется на другом устройстве.");
						return new JsonResult(new { success = false, errors = LoginErrors });
					}
					else if (forceLogin) {
						Console.WriteLine($"Отправка ForceLogout для пользователя {user.Id}");
						await _hubContext.Clients.User(user.Id.ToString()).SendAsync("ForceLogout");
						await _userService.ClearUserJwtIdAsync(user.Id.ToString());
						_context.Users.Update(user);
						await _context.SaveChangesAsync();
					}
				}

				// Обновляем JwtId для новой сессии
				user.JwtId = Guid.NewGuid().ToString();

				string? hardwareId = Request.Form["hardwareId"];
				if (!string.IsNullOrEmpty(hardwareId))
				{
					user.HardwareId = hardwareId;
				}

				user.LastEntry = DateTime.UtcNow;
				user.JwtId = Guid.NewGuid().ToString();

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