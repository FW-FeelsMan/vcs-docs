using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Configuration;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Pages;

public sealed class IndexModel : PageModel
{
	private readonly ApplicationDbContext _context;
	private readonly ILogger<IndexModel> _logger;
	private readonly UserDataPathOptions _userDataOptions;

	public string UserStorageRootPath { get; private set; } = "";
	public User? CurrentUser { get; private set; }
	public string AvatarUrl { get; private set; } = "";

	public IndexModel(
		ILogger<IndexModel> logger,
		ApplicationDbContext context,
		IOptions<UserDataPathOptions> userDataOptions)
	{
		_logger = logger;
		_context = context;
		_userDataOptions = userDataOptions.Value;
	}

	public async Task<IActionResult> OnGetAsync(CancellationToken ct)
	{
		if (User?.Identity?.IsAuthenticated != true)
			return RedirectToPage("/Login");

		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
			return RedirectToPage("/Login");

		await UpdateUserStatusAsync(userId, isOnline: true, ct);

		var username = User.Identity?.Name ?? string.Empty;
		ViewData["Username"] = username;

		CurrentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
		if (CurrentUser is null)
			return RedirectToPage("/Login");

		UserStorageRootPath = _userDataOptions.BasePath;
		ViewData["UserStorageBasePath"] = _userDataOptions.BasePath;

		var shortId = ToShortId(CurrentUser.Id);
		var avatarPath = Path.Combine(_userDataOptions.BasePath, $"u_{shortId}", "a", "avatar.jpg");

		AvatarUrl = System.IO.File.Exists(avatarPath)
			? $"/userdata/u_{shortId}/a/avatar.jpg?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"
			: "/images/default_avatar.png";

		ViewData["FullName"] = CurrentUser.FullName ?? "";
		ViewData["Email"] = CurrentUser.Email ?? "";
		ViewData["Login"] = CurrentUser.UserName ?? username;

		return Page();
	}

	public async Task<IActionResult> OnPostLogoutAsync(CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (!string.IsNullOrWhiteSpace(userId))
			await UpdateUserStatusAsync(userId, isOnline: false, ct);

		await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
		return RedirectToPage("/Login");
	}

	private async Task UpdateUserStatusAsync(string userId, bool isOnline, CancellationToken ct)
	{
		var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
		if (user is null)
			return;

		user.StatusOnline = isOnline ? 1 : 0;
		user.LastEntry = DateTime.UtcNow;

		try
		{
			await _context.SaveChangesAsync(ct);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to update user online status. UserId={UserId}, IsOnline={IsOnline}", userId, isOnline);
		}
	}

	private static string ToShortId(string? userId)
	{
		var compact = (userId ?? string.Empty).Replace("-", "");
		return compact.Length >= 8 ? compact[..8] : compact;
	}
}
