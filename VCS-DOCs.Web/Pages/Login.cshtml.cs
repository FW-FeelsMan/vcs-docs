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
using static VCS_DOCs.Models.Entities.OrganizationMemberRole;

namespace VCS_DOCs.Pages;

public sealed class LoginModel : PageModel
{
	private const int MaxUsernameLength = 20;
	private const int MaxPasswordLength = 100;
	private const int MinPasswordLength = 6;
	private const int MaxPathLength = 260;
	private const int MaxFailedAttempts = 5;
	private const string RegisterOrganizationLabel = "Зарегистрировать организацию";

	private static readonly TimeSpan LockWindow = TimeSpan.FromMinutes(10);
	private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);

	// простой email-regex (не RFC, но практично)
	private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LastAttemptUtc)> FailedLogins =
		new(StringComparer.Ordinal);

	private readonly ILogger<LoginModel> _logger;
	private readonly IHubContext<UserStatusHub> _hubContext;
	private readonly IUserService _userService;
	private readonly IWebHostEnvironment _webHostEnvironment;
	private readonly SignInManager<User> _signInManager;
	private readonly UserManager<User> _userManager;
	private readonly ApplicationDbContext _dbContext;

	public LoginModel(
		ILogger<LoginModel> logger,
		IHubContext<UserStatusHub> hubContext,
		IUserService userService,
		IWebHostEnvironment webHostEnvironment,
		SignInManager<User> signInManager,
		UserManager<User> userManager,
		ApplicationDbContext dbContext)
	{
		_logger = logger;
		_hubContext = hubContext;
		_userService = userService;
		_webHostEnvironment = webHostEnvironment;
		_signInManager = signInManager;
		_userManager = userManager;
		_dbContext = dbContext;
	}

	[BindProperty] public string Username { get; set; } = string.Empty;
	[BindProperty] public string Password { get; set; } = string.Empty;
	[BindProperty] public string ConfirmPassword { get; set; } = string.Empty;

	// NEW: Email для обычной регистрации (Частное лицо / Корп. учётка)
	[BindProperty] public string Email { get; set; } = string.Empty;

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
		var speciality = (Request.Form["speciality"].ToString() ?? string.Empty).Trim();
		var isOrg = string.Equals(speciality, RegisterOrganizationLabel, StringComparison.OrdinalIgnoreCase);

		var username = (Username ?? string.Empty).Trim();
		var password = Password ?? string.Empty;
		var confirmPassword = ConfirmPassword ?? string.Empty;
		var emailBasic = (Email ?? string.Empty).Trim();
		var ownerEmail = (Request.Form["OwnerEmail"].ToString() ?? string.Empty).Trim();
		var emailToUse = isOrg ? ownerEmail : emailBasic;

		var formatErrors = ValidateRegisterFormat(username, password, confirmPassword, emailToUse, isOrg, Request.Form);
		if (formatErrors.Count > 0)
			return JsonFailReg(formatErrors.ToArray());

		try
		{
			if (!isOrg)
				return await RegisterBasicAsync(speciality, username, emailToUse, password);

			return await RegisterOrganizationAsync(speciality, username, emailToUse, password, ct);
		}
		catch
		{
			return JsonFailReg("Произошла ошибка при регистрации.");
		}
	}

	private async Task<IActionResult> RegisterBasicAsync(string speciality, string username, string email, string password)
	{
		var existingUser = await _userManager.FindByNameAsync(username);
		if (existingUser is not null)
			return JsonFailReg("Пользователь с таким логином уже существует.");

		var byEmail = await _userManager.FindByEmailAsync(email);
		if (byEmail is not null)
			return JsonFailReg("Пользователь с таким Email уже существует.");

		var createResult = await CreateBaseUserAsync(speciality, username, email, password);
		if (!createResult.Success || createResult.User is null)
			return JsonFailReg(createResult.Errors.ToArray());

		IsRegistrationSuccessful = true;
		return new JsonResult(new { success = true });
	}

	private async Task<IActionResult> RegisterOrganizationAsync(string speciality, string username, string ownerEmail, string password, CancellationToken ct)
	{
		var orgName = (Request.Form["OrgName"].ToString() ?? string.Empty).Trim();
		var orgInn = (Request.Form["OrgInn"].ToString() ?? string.Empty).Trim();
		var orgEmail = (Request.Form["OrgEmail"].ToString() ?? string.Empty).Trim();
		var orgCountry = (Request.Form["OrgCountry"].ToString() ?? string.Empty).Trim();
		var orgAddress = (Request.Form["OrgAddress"].ToString() ?? string.Empty).Trim();
		var ownerFullName = (Request.Form["OwnerFullName"].ToString() ?? string.Empty).Trim();
		var ownerPosition = (Request.Form["OwnerPosition"].ToString() ?? string.Empty).Trim();

		var existingOrgByInn = await _dbContext.Organizations
			.AnyAsync(x => x.Country == orgCountry && x.Inn == orgInn, ct);
		if (existingOrgByInn)
			return JsonFailReg("Организация с таким ИНН в указанной стране уже зарегистрирована.");

		var existingOrgByEmail = await _dbContext.Organizations
			.AnyAsync(x => x.Email == orgEmail, ct);
		if (existingOrgByEmail)
			return JsonFailReg("Организация с таким Email уже зарегистрирована.");

		var userByName = await _userManager.FindByNameAsync(username);
		var userByEmail = await _userManager.FindByEmailAsync(ownerEmail);

		User? ownerUser;
		if (userByName is null && userByEmail is null)
		{
			var createResult = await CreateBaseUserAsync(speciality, username, ownerEmail, password, ownerFullName);
			if (!createResult.Success || createResult.User is null)
				return JsonFailReg(createResult.Errors.ToArray());

			ownerUser = createResult.User;
		}
		else if (userByName is not null)
		{
			if (userByName.IsDeleted)
				return JsonFailReg("Нельзя использовать удалённую учетную запись владельца.");

			if (!string.Equals(userByName.Email, ownerEmail, StringComparison.OrdinalIgnoreCase))
				return JsonFailReg("Указанный логин уже привязан к другому Email.");

			var passwordMatches = await _userManager.CheckPasswordAsync(userByName, password);
			if (!passwordMatches)
				return JsonFailReg("Для привязки существующего владельца укажите корректный пароль этого аккаунта.");

			ownerUser = userByName;
		}
		else
		{
			return JsonFailReg("Пользователь с таким Email уже существует под другим логином.");
		}

		var membershipRole = DetectMembershipRole(ownerPosition);
		var organization = new Organization
		{
			Id = Guid.NewGuid().ToString("D"),
			Name = orgName,
			Inn = orgInn,
			Email = orgEmail,
			Country = orgCountry,
			Address = orgAddress,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow,
			IsDeleted = false
		};

		var member = new OrganizationMember
		{
			OrganizationId = organization.Id,
			UserId = ownerUser.Id,
			Role = membershipRole,
			Position = string.IsNullOrWhiteSpace(ownerPosition) ? null : ownerPosition,
			CreatedAt = DateTime.UtcNow
		};

		_dbContext.Organizations.Add(organization);
		_dbContext.OrganizationMembers.Add(member);

		try
		{
			await _dbContext.SaveChangesAsync(ct);
		}
		catch (DbUpdateException dbEx)
		{
			return JsonFailReg(MapOrganizationConstraintError(dbEx));
		}

		IsRegistrationSuccessful = true;
		return new JsonResult(new { success = true });
	}

	private async Task<(bool Success, User? User, List<string> Errors)> CreateBaseUserAsync(string speciality, string username, string email, string password, string? fullName = null)
	{
		var newUser = new User
		{
			UserName = username,
			Email = email,
			FullName = string.IsNullOrWhiteSpace(fullName) ? "Не установлено" : fullName,
			Speciality = speciality,
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
			return (false, null, createRes.Errors.Select(x => x.Description).ToList());

		var roleMgr = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
		if (!await roleMgr.RoleExistsAsync(Roles.BaseUser))
			await roleMgr.CreateAsync(new IdentityRole(Roles.BaseUser));

		var roleRes = await _userManager.AddToRoleAsync(newUser, Roles.BaseUser);
		if (!roleRes.Succeeded)
		{
			await _userManager.DeleteAsync(newUser);
			return (false, null, roleRes.Errors.Select(x => x.Description).ToList());
		}

		var userFolderError = EnsureUserFolder(newUser.Id);
		if (userFolderError is not null)
		{
			await _userManager.DeleteAsync(newUser);
			return (false, null, new List<string> { userFolderError });
		}

		return (true, newUser, new List<string>());
	}

	private string? EnsureUserFolder(string userId)
	{
		var appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
		var shortUserId = ToShortUserId(userId);
		var userFolderName = $"u_{shortUserId}";
		var userDataPath = Path.Combine(appDataPath, userFolderName);

		if (userDataPath.Length >= MaxPathLength)
			return "Не удалось создать пользователя: путь к папке слишком длинный. Попробуйте более короткий логин.";

		Directory.CreateDirectory(userDataPath);
		return null;
	}

	private static OrganizationMemberRole DetectMembershipRole(string ownerPosition)
	{
		if (ownerPosition.Contains("директор", StringComparison.OrdinalIgnoreCase))
			return Director;

		return Owner;
	}

	private static string MapOrganizationConstraintError(DbUpdateException ex)
	{
		var text = ex.InnerException?.Message ?? ex.Message;
		if (text.Contains("Organizations.Country", StringComparison.OrdinalIgnoreCase) || text.Contains("Organizations.Inn", StringComparison.OrdinalIgnoreCase))
			return "Организация с таким ИНН в указанной стране уже зарегистрирована.";

		if (text.Contains("Organizations.Email", StringComparison.OrdinalIgnoreCase))
			return "Организация с таким Email уже зарегистрирована.";

		if (text.Contains("OrganizationMembers", StringComparison.OrdinalIgnoreCase))
			return "Пользователь уже состоит в этой организации.";

		return "Не удалось завершить регистрацию организации из-за конфликта данных.";
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

	private static List<string> ValidateRegisterFormat(string username, string password, string confirmPassword, string email, bool isOrg, IFormCollection form)
	{
		var errors = new List<string>();

		if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(confirmPassword))
			errors.Add("Имя пользователя и пароль обязательны.");

		if (username.Length > MaxUsernameLength)
			errors.Add("Имя пользователя не должно превышать 20 символов.");

		if (!string.IsNullOrWhiteSpace(username) && !UsernameRegex.IsMatch(username))
			errors.Add("Имя пользователя может содержать только латиницу, цифры, точку, подчёркивание и дефис.");

		if (password.Length > MaxPasswordLength || confirmPassword.Length > MaxPasswordLength)
			errors.Add("Пароль не должен превышать 100 символов.");

		if (!string.IsNullOrEmpty(password) && password.Length < MinPasswordLength)
			errors.Add("Пароль должен быть не менее 6 символов.");

		if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
			errors.Add("Пароли не совпадают.");

		if (string.IsNullOrWhiteSpace(email))
			errors.Add("Email обязателен.");

		if (email.Length > 120)
			errors.Add("Email не должен превышать 120 символов.");

		if (!string.IsNullOrWhiteSpace(email) && !EmailRegex.IsMatch(email))
			errors.Add("Некорректный формат Email.");

		if (isOrg)
		{
			var orgName = (form["OrgName"].ToString() ?? string.Empty).Trim();
			var orgInn = (form["OrgInn"].ToString() ?? string.Empty).Trim();
			var orgEmail = (form["OrgEmail"].ToString() ?? string.Empty).Trim();
			var orgCountry = (form["OrgCountry"].ToString() ?? string.Empty).Trim();
			var orgAddress = (form["OrgAddress"].ToString() ?? string.Empty).Trim();

			if (string.IsNullOrWhiteSpace(orgName)) errors.Add("Название организации обязательно.");
			if (string.IsNullOrWhiteSpace(orgInn)) errors.Add("ИНН организации обязателен.");
			if (string.IsNullOrWhiteSpace(orgEmail)) errors.Add("Email организации обязателен.");
			if (string.IsNullOrWhiteSpace(orgCountry)) errors.Add("Страна организации обязательна.");
			if (string.IsNullOrWhiteSpace(orgAddress)) errors.Add("Адрес организации обязателен.");

			if (orgName.Length > 120) errors.Add("Название организации не должно превышать 120 символов.");
			if (orgInn.Length > 20) errors.Add("ИНН организации не должен превышать 20 символов.");
			if (orgEmail.Length > 120) errors.Add("Email организации не должен превышать 120 символов.");
			if (orgCountry.Length > 80) errors.Add("Страна организации не должна превышать 80 символов.");
			if (orgAddress.Length > 200) errors.Add("Адрес организации не должен превышать 200 символов.");

			if (!string.IsNullOrWhiteSpace(orgEmail) && !EmailRegex.IsMatch(orgEmail))
				errors.Add("Некорректный формат Email организации.");

			if (!string.IsNullOrWhiteSpace(orgInn) && !orgInn.All(ch => char.IsDigit(ch) || ch == '-' || ch == ' '))
				errors.Add("ИНН организации должен содержать только цифры, пробел или дефис.");
		}

		return errors.Distinct().ToList();
	}

	private JsonResult JsonFail(string error) => new(new { success = false, errors = new[] { error } });

	private JsonResult JsonFailReg(params string[] errors) => new(new { success = false, errors });

	private static string ToShortUserId(string userId)
	{
		var compact = userId.Replace("-", "");
		return compact.Length >= 8 ? compact[..8] : compact;
	}
}
