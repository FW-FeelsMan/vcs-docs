using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using VCS_DOCs.Upload.Core;

namespace VCS_DOCs.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Authorize]
	public class UploadController : ControllerBase
	{
		private readonly UploadManager _uploadManager;

		public UploadController(UploadManager uploadManager)
		{
			_uploadManager = uploadManager;
		}

		private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

		[HttpPost("chunk")]
		[DisableRequestSizeLimit]
		public async Task<IActionResult> UploadChunk(
			[FromForm] IFormFile chunk,
			[FromForm] string hash,
			[FromForm] int chunkIndex,
			[FromForm] int totalChunks,
			[FromForm] long fileSize,
			[FromForm] int? replaceVersion,
			[FromForm] string fileName)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized("Пользователь не найден");

			var result = await _uploadManager.HandleChunkUploadAsync(userId, chunk, hash, chunkIndex, totalChunks, fileSize, replaceVersion, fileName);
			return result;
		}
		[HttpGet("download/{fileId}/{version}")]
		public async Task<IActionResult> DownloadFile(Guid fileId, int version)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			var file = await _uploadManager.GetFileVersionAsync(userId, fileId, version);
			if (file == null)
				return NotFound();

			return File(file.Content, "application/octet-stream", file.FileName);
		}
		[HttpDelete("delete/{fileId}/{version}")]
		public async Task<IActionResult> DeleteFile(Guid fileId, int version)
		{
			var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			var result = await _uploadManager.DeleteFileVersionAsync(userId, fileId, version);
			if (!result)
				return BadRequest("Не удалось удалить файл");

			return Ok(new { status = "deleted" });
		}
	}
}
