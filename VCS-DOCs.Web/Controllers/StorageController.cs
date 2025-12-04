using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Upload.Core.Services;

namespace VCS_DOCs.Controllers;

[ApiController]
[Route("api/storage")]
[Authorize]
public sealed class StorageController : ControllerBase
{
	private const long DefaultLimitBytes = 10L * 1024 * 1024 * 1024;

	private readonly IUserFileService _fileService;
	private readonly ApplicationDbContext _db;

	public StorageController(IUserFileService fileService, ApplicationDbContext db)
	{
		_fileService = fileService;
		_db = db;
	}

	[HttpGet("files")]
	public async Task<IActionResult> GetFiles(CancellationToken ct)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrWhiteSpace(userId))
			return Unauthorized();

		var files = await _fileService.GetFilesForUserAsync(userId);

		var usedBytes = files.Sum(f => f.FileSize);
		var tempBytes = 0L;

		var limitBytes = await _db.Users
			.Where(u => u.Id == userId)
			.Select(u => (long?)u.StorageLimitBytes)
			.FirstOrDefaultAsync(ct) ?? DefaultLimitBytes;

		return Ok(new
		{
			files,
			usedBytes,
			tempBytes,
			limitBytes
		});
	}
}
