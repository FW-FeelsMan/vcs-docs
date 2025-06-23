using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VCS_DOCs.Upload.Core.Services;

namespace VCS_DOCs.Controllers
{
	[ApiController]
	[Route("api/storage")]
	[Authorize]
	public class StorageController : ControllerBase
	{
		private readonly IUserFileService _fileService;

		public StorageController(IUserFileService fileService)
		{
			_fileService = fileService;
		}

		[HttpGet("files")]
		public async Task<IActionResult> GetFiles()
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			var files = await _fileService.GetFilesForUserAsync(userId);

			var usedBytes = files.Sum(f => f.FileSize);
			var tempBytes = 0L;
			var limitBytes = 500 * 1024 * 1024;

			return Ok(new
			{
				files,
				usedBytes,
				tempBytes,
				limitBytes
			});
		}
	}
}