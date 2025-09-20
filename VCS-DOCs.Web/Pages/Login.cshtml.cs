// Pages/Login.cshtml.cs (Web)
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;
using VCS_DOCs.Data.Hubs;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Utilities;

namespace VCS_DOCs.Pages
{
    public class LoginModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LoginModel> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<UserStatusHub> _hubContext;
        private readonly IUserService _userService;
        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly SignInManager<User> _signInManager;
        private readonly UserManager<User> _userManager;

        public LoginModel(
            ApplicationDbContext context,
            ILogger<LoginModel> logger,
            IServiceProvider serviceProvider,
            IHubContext<UserStatusHub> hubContext,
            IUserService userService,
            IWebHostEnvironment webHostEnvironment,
            SignInManager<User> signInManager,
            UserManager<User> userManager)
        {
            _context = context;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
            _userService = userService;
            _webHostEnvironment = webHostEnvironment;
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [BindProperty] public string Username { get; set; } = string.Empty;
        [BindProperty] public string Password { get; set; } = string.Empty;

        public List<string> LoginErrors
        {
            get; set;
        }
        public List<string> RegistrationErrors
        {
            get; set;
        }
        public bool IsRegistrationSuccessful
        {
            get; set;
        }
        public string? ErrorMessage
        {
            get; set;
        }
        public List<string> Specialities
        {
            get; set;
        }

        private static readonly ConcurrentDictionary<string, (int Attempts, DateTime LastAttempt)> FailedLogins
            = new ConcurrentDictionary<string, (int, DateTime)>();

        public async Task<IActionResult> OnPostLoginAsync()
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            LoginErrors ??= new List<string>();

            // Триммим только логин; ПАРОЛЬ НЕ ТРОГАЕМ
            Username = (Username ?? string.Empty).Trim();
            Password = Password ?? string.Empty;

            _logger.LogInformation("Login attempt for {U}, pwd len={L}", Username, Password?.Length ?? 0);

            if (ip == null)
                return new JsonResult(new { success = false, errors = new List<string> { "Ошибка авторизации." } });

            if (FailedLogins.TryGetValue(ip, out var data))
            {
                if (data.Attempts >= 5 && (DateTime.Now - data.LastAttempt).TotalMinutes < 10)
                    return new JsonResult(new { success = false, errors = new List<string> { "Слишком много неудачных попыток. Попробуйте позже." } });
            }

            // --- Серверные проверки формата/длины ---
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password))
            {
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Имя пользователя и пароль обязательны." } });
            }
            if (Username.Length > 20)
            {
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Логин не более 20 символов." } });
            }
            if (!Regex.IsMatch(Username, @"^[a-zA-Z0-9._-]+$"))
            {
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Логин может содержать только латиницу, цифры, точку, подчёркивание и дефис." } });
            }

            if (Password.Length > 100)
            {
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Пароль не более 100 символов." } });
            }
            // ---------------------------------------

            // Ищем пользователя через Identity
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == Username);
            if (user == null || user.IsDeleted)
            {
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Неверное имя пользователя, пароль или аккаунт был удалён." } });
            }

            // Проверяем пароль ТОЛЬКО через Identity (AQAAAA... совместимо)
            bool passwordOk = false;
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
                FailedLogins[ip] = (data.Attempts + 1, DateTime.Now);
                return new JsonResult(new { success = false, errors = new List<string> { "Неверное имя пользователя, пароль или аккаунт был удалён." } });
            }

            if (user.Access == 0)
                return new JsonResult(new { success = false, errors = new List<string> { "Учетная запись не активирована." } });

            // Анти-дублирующий вход
            bool forceLogin = Request.Form["ForceLogin"].ToString().ToLower() == "true";
            if (user.StatusOnline == 1 && !forceLogin)
            {
                return new JsonResult(new
                {
                    success = false,
                    errors = new List<string> { "Этот аккаунт уже используется на другом устройстве." }
                });
            }

            if (user.StatusOnline == 1 && forceLogin)
            {
                await _hubContext.Clients.User(user.Id).SendAsync("ForceLogout");
                await _userService.ClearUserJwtIdAsync(user.Id);
            }

            user.JwtId = Guid.NewGuid().ToString();
            user.HardwareId = Request.Form["hardwareId"].ToString() ?? user.HardwareId;
            user.LastEntry = DateTime.Now;
            user.StatusOnline = 1;
            await _userManager.UpdateAsync(user);

            var extraClaims = new List<Claim> { new Claim("web_sid", user.JwtId ?? string.Empty) };
            await _signInManager.SignInWithClaimsAsync(user, isPersistent: true, extraClaims);

            FailedLogins.TryRemove(ip, out _);
            _logger.LogInformation($"Пользователь {user.UserName} вошел в систему.");

            return new JsonResult(new { success = true });
        }

        // --- остальной код (регистрация/OnGet) без изменений ---
        public async Task<IActionResult> OnPostRegisterAsync()
        {
            RegistrationErrors ??= new List<string>();

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

            if (!Regex.IsMatch(Username, @"^[a-zA-Z0-9._-]+$"))
            {
                RegistrationErrors.Add("Имя пользователя может содержать только латиницу, цифры, точку, подчёркивание и дефис.");
                return new JsonResult(new { success = false, errors = RegistrationErrors });
            }

            if (Password.Length > 100) // синхронизировано с логином
            {
                RegistrationErrors.Add("Пароль не должен превышать 100 символов.");
                return new JsonResult(new { success = false, errors = RegistrationErrors });
            }

            if (Password.Length < 6)
            {
                RegistrationErrors.Add("Пароль должен быть не менее 6 символов.");
                return new JsonResult(new { success = false, errors = RegistrationErrors });
            }

            try
            {
                var existingUser = await _userManager.FindByNameAsync(Username);
                if (existingUser != null)
                {
                    RegistrationErrors.Add("Пользователь с таким логином уже существует.");
                    return new JsonResult(new { success = false, errors = RegistrationErrors });
                }

                var newUser = new User
                {
                    UserName = Username,
                    Speciality = Request.Form["speciality"],
                    StatusOnline = 0,
                    HardwareId = null,
                    LastEntry = null,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                    IsDeleted = false,
                    Access = 0,
                    StorageLimitBytes = 10L * 1024 * 1024 * 1024
                };

                var createRes = await _userManager.CreateAsync(newUser, Password);
                if (!createRes.Succeeded)
                {
                    foreach (var err in createRes.Errors)
                        RegistrationErrors.Add(err.Description);
                    return new JsonResult(new { success = false, errors = RegistrationErrors });
                }

                var roleMgr = HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
                if (!await roleMgr.RoleExistsAsync(Roles.BaseUser))
                    await roleMgr.CreateAsync(new IdentityRole(Roles.BaseUser));

                var roleRes = await _userManager.AddToRoleAsync(newUser, Roles.BaseUser);
                if (!roleRes.Succeeded)
                {
                    foreach (var err in roleRes.Errors)
                        RegistrationErrors.Add(err.Description);
                    await _userManager.DeleteAsync(newUser);
                    return new JsonResult(new { success = false, errors = RegistrationErrors });
                }

                string appDataPath = Path.Combine(_webHostEnvironment.ContentRootPath, "Data", "userData");
                string shortUserId = newUser.Id.Replace("-", "").Substring(0, 8);
                string userFolderName = $"u_{shortUserId}";
                string userDataPath = Path.Combine(appDataPath, userFolderName);

                int fullFolderPathLength = userDataPath.Length;
                if (fullFolderPathLength >= MaxPathLength)
                {
                    await _userManager.DeleteAsync(newUser);
                    RegistrationErrors.Add("Не удалось создать пользователя: путь к папке слишком длинный. Попробуйте более короткий логин.");
                    return new JsonResult(new { success = false, errors = RegistrationErrors });
                }

                if (!Directory.Exists(userDataPath))
                {
                    Directory.CreateDirectory(userDataPath);
                }

                IsRegistrationSuccessful = true;
                return new JsonResult(new { success = true });
            }
            catch
            {
                RegistrationErrors.Add("Произошла ошибка при регистрации.");
                return new JsonResult(new { success = false, errors = RegistrationErrors });
            }
        }

        public async Task<IActionResult> OnPostAsync(string action)
        {
            if (action == "Login") return await OnPostLoginAsync();
            else if (action == "Register") return await OnPostRegisterAsync();
            return Page();
        }

        public void OnGet()
        {
            var configPath = Path.Combine(Directory.GetCurrentDirectory(), "Utilities", "Config.ini");
            Specialities = ConfigReader.GetSpecialities(configPath);

            RegistrationErrors ??= new List<string>();
            LoginErrors ??= new List<string>();
        }
    }
}