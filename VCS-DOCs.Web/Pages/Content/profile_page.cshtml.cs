using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Pages.Content;

public sealed class profile_pageModel : PageModel
{
	private const long StorageLimitBytes = 10L * 1024 * 1024 * 1024;

	private readonly ApplicationDbContext _context;
	private readonly IAntiforgery _antiforgery;
	private readonly UserManager<User> _userManager;
	private readonly UserDataPathOptions _options;
	private readonly UserStoragePaths _userPaths;

	public string AvatarPath { get; private set; } = "/images/default_avatar.png";
	public double UsedGb { get; private set; }
	public double FreeGb { get; private set; }
	public User? CurrentUser { get; private set; }

	private static readonly Regex ValidInputRegex =
		new(@"^[a-zA-Zа-яА-Я0-9@'""\-\s]{1,30}$", RegexOptions.Compiled);

	public profile_pageModel(
		ApplicationDbContext context,
		IAntiforgery antiforgery,
		UserManager<User> userManager,
		IOptions<UserDataPathOptions> options,
		UserStoragePaths userPaths)
	{
		_context = context;
		_antiforgery = antiforgery;
		_userManager = userManager;
		_options = options.Value;
		_userPaths = userPaths;
	}

	public async Task OnGetAsync(CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId)) return;

		CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
		if (CurrentUser is null) return;

		var shortUserId = ToShortId(userId);
		var userDir = Path.Combine(_options.BasePath, $"u_{shortUserId}");

		var usedBytes = Directory.Exists(userDir)
			? Directory.GetFiles(userDir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
			: 0L;

		var freeBytes = Math.Max(0L, StorageLimitBytes - usedBytes);

		UsedGb = Math.Round(usedBytes / (1024d * 1024 * 1024), 2);
		FreeGb = Math.Round(freeBytes / (1024d * 1024 * 1024), 2);

		var avatarFsPath = _userPaths.GetAvatarPath(shortUserId);
		if (System.IO.File.Exists(avatarFsPath))
		{
			var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			AvatarPath = $"/userdata/u_{shortUserId}/a/avatar.jpg?v={ts}";
		}
		else
		{
			AvatarPath = "/images/default_avatar.png";
		}
	}

	public async Task<IActionResult> OnPostUpdateUserDataAsync([FromBody] UpdateUserRequest request, CancellationToken ct)
	{
		if (!await TryValidateAntiforgeryAsync())
			return new JsonResult(new { success = false, error = "Неверный токен безопасности" });

		if (User.Identity?.IsAuthenticated != true)
			return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });

		if (request is null || string.IsNullOrWhiteSpace(request.Field))
			return new JsonResult(new { success = false, error = "Некорректные данные" });

		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
			return new JsonResult(new { success = false, error = "Пользователь не найден" });

		var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
		if (user is null)
			return new JsonResult(new { success = false, error = "Пользователь не найден" });

		var field = request.Field.Trim();
		var value = (request.Value ?? string.Empty).Trim();

		if (string.Equals(value, "Не установлено", StringComparison.OrdinalIgnoreCase))
			value = string.Empty;

		var error = ApplyUserField(user, field, value);
		if (error is not null)
			return new JsonResult(new { success = false, error });

		user.UpdatedAt = DateTime.UtcNow;

		try
		{
			await _context.SaveChangesAsync(ct);
			return new JsonResult(new { success = true });
		}
		catch (DbUpdateException ex)
		{
			return new JsonResult(new { success = false, error = ex.InnerException?.Message ?? ex.Message });
		}
	}

	public async Task<IActionResult> OnPostDeleteAccountAsync([FromForm] DeleteAccountRequest request, CancellationToken ct)
	{
		if (!await TryValidateAntiforgeryAsync())
			return new JsonResult(new { success = false, error = "Неверный токен безопасности" });

		if (User.Identity?.IsAuthenticated != true)
			return new JsonResult(new { success = false, error = "Пользователь не аутентифицирован" });

		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
			return new JsonResult(new { success = false, error = "Пользователь не найден" });

		var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
		if (user is null)
			return new JsonResult(new { success = false, error = "Пользователь не найден" });

		var password = (request?.Password ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(password))
			return new JsonResult(new { success = false, error = "Введите пароль" });

		var passwordOk = await _userManager.CheckPasswordAsync(user, password);
		if (!passwordOk)
			return new JsonResult(new { success = false, error = "Неверный пароль" });

		user.IsDeleted = true;
		user.UpdatedAt = DateTime.UtcNow;

		await _userManager.UpdateSecurityStampAsync(user);

		try
		{
			await _context.SaveChangesAsync(ct);
			await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
			return new JsonResult(new { success = true });
		}
		catch (Exception ex)
		{
			return new JsonResult(new { success = false, error = ex.Message });
		}
	}

	public async Task<IActionResult> OnPostUploadAvatarAsync(CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
			return new JsonResult(new { success = false, error = "Пользователь не найден" });

		var file = Request.Form.Files["avatar"];
		if (file is null || file.Length == 0)
			return new JsonResult(new { success = false, error = "Файл не получен" });

		var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
		if (ext is not (".jpg" or ".jpeg" or ".png"))
			return new JsonResult(new { success = false, error = "Неверный формат файла" });

		var shortUserId = ToShortId(userId);
		var avatarPath = _userPaths.GetAvatarPath(shortUserId);

		Directory.CreateDirectory(Path.GetDirectoryName(avatarPath)!);

		var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		try
		{
			await using var stream = new FileStream(avatarPath, FileMode.Create, FileAccess.Write, FileShare.None);
			await file.CopyToAsync(stream, ct);

			return new JsonResult(new { success = true, userId = shortUserId, timestamp });
		}
		catch (Exception ex)
		{
			return new JsonResult(new { success = false, error = ex.Message });
		}
	}

	private async Task<bool> TryValidateAntiforgeryAsync()
	{
		try
		{
			await _antiforgery.ValidateRequestAsync(HttpContext);
			return true;
		}
		catch (AntiforgeryValidationException)
		{
			return false;
		}
	}

	private static string? ApplyUserField(User user, string field, string value)
	{
		switch (field)
		{
			case "DateOfBirth":
				if (string.IsNullOrWhiteSpace(value))
					return "Дата не указана";

				if (!DateTime.TryParseExact(value, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
					return "Неверный формат даты";

				user.DateOfBirth = parsedDate.ToString("dd.MM.yyyy");
				return null;

			case "FullName":
				return TrySetText(value, 30, v => user.FullName = v);

			case "Organization":
				return TrySetText(value, 30, v => user.Organization = v);

			case "Department":
				return TrySetText(value, 30, v => user.Department = v);

			case "Speciality":
				return TrySetText(value, 30, v => user.Speciality = v);

			case "Email":
				if (value.Length > 254)
					return "Слишком длинный e-mail";

				if (string.IsNullOrWhiteSpace(value))
				{
					user.Email = null;
					user.NormalizedEmail = null;
					user.EmailConfirmed = false;
					return null;
				}

				try { _ = new MailAddress(value); }
				catch { return "Почта указана неверно"; }

				user.Email = value;
				user.NormalizedEmail = value.ToUpperInvariant();
				user.EmailConfirmed = false;
				return null;

			default:
				return "Недопустимое поле";
		}
	}

	private static string? TrySetText(string value, int maxLen, Action<string> setter)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > maxLen)
			return "Некорректные данные";

		if (!ValidInputRegex.IsMatch(value))
			return "Недопустимые символы";

		setter(value);
		return null;
	}

	private static string ToShortId(string userId)
	{
		var cleaned = userId.Replace("-", "");
		return cleaned.Length >= 8 ? cleaned[..8] : cleaned;
	}
}

public sealed class UpdateUserRequest
{
	public string? Field { get; set; }
	public string? Value { get; set; }
}

public sealed class DeleteAccountRequest
{
	public string? Password { get; set; }
}
