using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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
				LoginErrors.Add("��� ������������ � ������ �����������.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}

			try
			{
				var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == Username);
				if (user == null || !BCrypt.Net.BCrypt.Verify(Password, user.Password))
				{
					LoginErrors.Add("�������� ��� ������������ ��� ������.");
					return new JsonResult(new { success = false, errors = LoginErrors });
				}


				if (user.Access == 0)
				{
					LoginErrors.Add("������� ������ �� ������������.");
					return new JsonResult(new { success = false, errors = LoginErrors });
				}

				string hardwareId = Request.Form["hardwareId"];

				user.HardwareId = hardwareId;
				user.LastEntry = DateTime.Now;
				_context.Users.Update(user);
				await _context.SaveChangesAsync();

				HttpContext.Response.Cookies.Append("AuthUser", user.Username, new Microsoft.AspNetCore.Http.CookieOptions
				{
					HttpOnly = true,
					Secure = true,
					SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict
				});

				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "An error occurred during login.");
				LoginErrors.Add("��������� ������ ��� ����� � �������.");
				return new JsonResult(new { success = false, errors = LoginErrors });
			}
		}
		public async Task<IActionResult> OnPostRegisterAsync()
		{
			if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
			{
				RegistrationErrors.Add("��� ������������ � ������ �����������.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			if (Password.Length < 6)
			{
				RegistrationErrors.Add("������ ������ ��������� �� ����� 6 ��������.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == Username);
			if (existingUser != null)
			{
				RegistrationErrors.Add("������������ ��� ����������.");
				return new JsonResult(new { success = false, errors = RegistrationErrors });
			}

			try
			{
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

				// �������� ����� ������������
				string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", $"userData_{Username}");
				if (!Directory.Exists(appDataPath))
				{
					Directory.CreateDirectory(appDataPath);
				}

				IsRegistrationSuccessful = true;
				return new JsonResult(new { success = true });
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "������ �� ����� �����������.");
				RegistrationErrors.Add("��������� ������ ��� �����������.");
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
