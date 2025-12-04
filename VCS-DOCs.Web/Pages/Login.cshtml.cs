using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Utilities;

namespace VCS_DOCs.Pages;

public sealed class LoginModel : PageModel
{
	private const int MaxUsernameLength = 20;
	private const int MaxPasswordLength = 100;
	private const int MinPasswordLength = 6;
	private const int MaxPathLength = 260;
	private const int MaxFailedAttempts = 5;
	private static readonly TimeSpan LockWindow = TimeSpan.FromMinutes(10);
	private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

	private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LastAttemptUtc)> FailedLogins =
		new(StringComparer.Ordinal);

	private readonly ILogger<LoginModel> _logger;
	private readonly IHubContext<UserStatusHub> _hubContext;
	private readonly IUserService _userService;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly SignInManager<User> _signInManager;
	private readonly UserManager<User> _userManager;

	public LoginModel(
		ILogger<LoginModel> logger,
		IHubContext<UserStatusHub> hubContext,
		IUserService userService,
		IWebHostEnvironment webHostEnvironment,
		SignInManager<User> signInManager,
		UserManager<User> userManager)
	{
		_logger = logger;
		_hubContext = hubContext;
		_userService = userService;
		_webHostEnvironment = webHostEnvironment;
		_signInManager = signInManager;
		_userManager = userManager;
	}

	[BindProperty] public string Username { get; set; } = string.Empty;
	[BindProperty] public string Password { get; set; } = string.Empty;

	public List<string> LoginErrors { get; set; } = new();
	public List<string> RegistrationErrors { get; set; } = new();
	public bool IsRegistrationSuccessful { get; set; }
	public string? ErrorMessage { get; set; }
	public List<string> Specialities { get; set; } = new();

	public async Task<IActionResult> OnPostLoginAsync(CancellationToken ct = default)
	{
		var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
		if (string.IsNullOrWhiteSpace(ip))
			return JsonFail("Ошибка авторизации.");

		Username = (Username ?? string.Empty).Trim();
		Password ??= string.Empty;

		_logger.LogInformation("Login attempt for {Username}, pwd len={Len}, ip={Ip}", Username, Password.Length, ip);

		if (IsLockedOut(ip))
			return JsonFail("Слишком много неудачных попыток. Попробуйте позже.");

		var formatError = ValidateLoginFormat(Username, Password);
		if (formatError is not null)
		{
			RegisterFail(ip);
			return JsonFail(formatError);
		}

		var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == Username, ct);
		if (user is null || user.IsDeleted)
		{
			RegisterFail(ip);
			return JsonFail("Неверное имя пользователя, пароль или аккаунт был удалён.");
		}

		bool passwordOk;
		try
		{
			passwordOk = await _userManager.CheckPasswordAsync(user, Password);
		}
		catch
		{
			passwordOk = false;
		}

		if (!passwordOk)
		{
			RegisterFail(ip);
			return JsonFail("Неверное имя пользователя, пароль или аккаунт был удалён.");
		}

		if (user.Access == 0)
			return JsonFail("Учетная запись не активирована.");

		var forceLogin = string.Equals(Request.Form["ForceLogin"], "true", StringComparison.OrdinalIgnoreCase);

		if (user.StatusOnline == 1 && !forceLogin)
			return JsonFail("Этот аккаунт уже используется на другом устройстве.");

		if (user.StatusOnline == 1 && forceLogin)
		{
			await _hubContext.Clients.User(user.Id).SendAsync("ForceLogout", cancellationToken: ct);
			await _userService.ClearUserJwtIdAsync(user.Id);
		}

		user.JwtId = Guid.NewGuid().ToString();
		user.HardwareId = Request.Form["hardwareId"].ToString() ?? user.HardwareId;
		user.LastEntry = DateTime.UtcNow;
		user.StatusOnline = 1;

		await _userManager.UpdateAsync(user);

		var extraClaims = new List<Claim> { new("web_sid", user.JwtId ?? string.Empty) };
		await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

		FailedLogins.TryRemove(ip, out _);
		_logger.LogInformation("User {Username} signed in.", user.UserName);

		return new JsonResult(new { success = true });
	}

	public async Task<IActionResult> OnPostRegisterAsync(CancellationToken ct = default)
	{
		var username = (Username ?? string.Empty).Trim();
		var password = Password ?? string.Empty;

		var formatError = ValidateRegisterFormat(username, password);
		if (formatError is not null)
			return JsonFailReg(formatError);

		try
		{
			var existingUser = await _userManager.FindByNameAsync(username);
			if (existingUser is not null)
				return JsonFailReg("Пользователь с таким логином уже существует.");

			var newUser = new User
			{
				UserName = username,
				Speciality = Request.Form["speciality"],
				StatusOnline = 0,
				HardwareId = null,
				LastEntry = null,
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow,
				IsDeleted = false,
				Access = 0,
				StorageLimitBytes = 10L * 1024 * 1024 * 1024
			};

			var createRes = await _userManager.CreateAsync(newUser, password);
			if (!createRes.Succeeded)
				return JsonFailReg(createRes.Errors.Select(e => e.Description).ToArray());

			var roleMgr = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
			if (!await roleMgr.RoleExistsAsync(Roles.BaseUser))
				await roleMgr.CreateAsync(new IdentityRole(Roles.BaseUser));

			var roleRes = await _userManager.AddToRoleAsync(newUser, Roles.BaseUser);
			if (!roleRes.Succeeded)
			{
				await _userManager.DeleteAsync(newUser);
				return JsonFailReg(roleRes.Errors.Select(e => e.Description).ToArray());
			}

			var appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
			var shortUserId = ToShortUserId(newUser.Id);
			var userFolderName = $"u_{shortUserId}";
			var userDataPath = Path.Combine(appDataPath, userFolderName);

			if (userDataPath.Length >= MaxPathLength)
			{
				await _userManager.DeleteAsync(newUser);
				return JsonFailReg("Не удалось создать пользователя: путь к папке слишком длинный. Попробуйте более короткий логин.");
			}

			Directory.CreateDirectory(userDataPath);

			IsRegistrationSuccessful = true;
			return new JsonResult(new { success = true });
		}
		catch
		{
			return JsonFailReg("Произошла ошибка при регистрации.");
		}
	}

	public async Task<IActionResult> OnPostAsync(string action, CancellationToken ct)
	{
		return action switch
		{
			"Login" => await OnPostLoginAsync(ct),
			"Register" => await OnPostRegisterAsync(ct),
			_ => Page()
		};
	}

	public void OnGet()
	{
		var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Utilities", "Config.ini");
		Specialities = ConfigReader.GetSpecialities(configPath);
	}

	private bool IsLockedOut(string ip)
	{
		if (!FailedLogins.TryGetValue(ip, out var entry))
			return false;

		if (entry.Attempts < MaxFailedAttempts)
			return false;

		return (DateTime.UtcNow - entry.LastAttemptUtc) < LockWindow;
	}

	private void RegisterFail(string ip)
	{
		FailedLogins.AddOrUpdate(
			ip,
			_ => (1, DateTime.UtcNow),
			(_, cur) => (cur.Attempts + 1, DateTime.UtcNow));
	}

	private static string? ValidateLoginFormat(string username, string password)
	{
		if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
			return "Имя пользователя и пароль обязательны.";

		if (username.Length > MaxUsernameLength)
			return "Логин не более 20 символов.";

		if (!UsernameRegex.IsMatch(username))
			return "Логин может содержать только латиницу, цифры, точку, подчёркивание и дефис.";

		if (password.Length > MaxPasswordLength)
			return "Пароль не более 100 символов.";

		return null;
	}

	private static string? ValidateRegisterFormat(string username, string password)
	{
		if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
			return "Имя пользователя и пароль обязательны.";

		if (username.Length > MaxUsernameLength)
			return "Имя пользователя не должно превышать 20 символов.";

		if (!UsernameRegex.IsMatch(username))
			return "Имя пользователя может содержать только латиницу, цифры, точку, подчёркивание и дефис.";

		if (password.Length > MaxPasswordLength)
			return "Пароль не должен превышать 100 символов.";

		if (password.Length < MinPasswordLength)
			return "Пароль должен быть не менее 6 символов.";

		return null;
	}

	private JsonResult JsonFail(string error) => new(new { success = false, errors = new[] { error } });

	private JsonResult JsonFailReg(params string[] errors) => new(new { success = false, errors });

	private static string ToShortUserId(string userId)
	{
		var compact = userId.Replace("-", "");
		return compact.Length >= 8 ? compact[..8] : compact;
	}
}
