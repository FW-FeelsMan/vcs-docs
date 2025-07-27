using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core;
using VCS_DOCs.Upload.Core.Services.Tasks;

namespace VCS_DOCs.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	[Authorize]
	public class UploadController : ControllerBase
	{
		private readonly UploadManager _uploadManager;
		private readonly ChunkHashService _chunkHasher;

		public UploadController(UploadManager uploadManager, ChunkHashService chunkHasher)
		{
			_uploadManager = uploadManager;
			_chunkHasher = chunkHasher;
		}

		private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

		private string GetRequiredShortUserId()
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				throw new UnauthorizedAccessException("Пользователь не найден");
			return userId.Replace("-", "").Substring(0, 8);
		}

        [HttpPost("chunk")]
        [DisableRequestSizeLimit]
        public async Task<IActionResult> UploadChunk(
    [FromForm] IFormFile chunk,
    [FromForm] string hash,
    [FromForm] int chunkIndex,
    [FromForm] int totalChunks,
    [FromForm] long fileSize,
    [FromForm] int? replaceVersion,
    [FromForm] string fileName,
    [FromForm] Guid? sessionId)
        {
            try
            {
                var shortUserId = GetRequiredShortUserId();

                var result = await _uploadManager.HandleChunkUploadAsync(
                    shortUserId, chunk, hash, chunkIndex, totalChunks, fileSize, replaceVersion, fileName, sessionId); // 👈 прокинули

                using var stream = chunk.OpenReadStream();
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hashBytes = md5.ComputeHash(stream);
                var chunkHash = Convert.ToHexString(hashBytes);

                _chunkHasher.SaveChunkHash(shortUserId, result.SessionId, chunkIndex, chunkHash);

                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        }


        [HttpGet("upload-status")]
		public async Task<IActionResult> UploadStatus([FromQuery] string fileHash)
		{
			try
			{
				var shortUserId = GetRequiredShortUserId();
				var result = await _uploadManager.GetOngoingSessionsByHashAsync(shortUserId, fileHash);

				if (result == null)
					return Ok(new { found = false });

				return Ok(new
				{
					found = true,
					sessionId = result.Value.SessionId,
					uploaded = result.Value.UploadedChunks
				});
			}
			catch (UnauthorizedAccessException ex)
			{
				return Unauthorized(ex.Message);
			}
		}

		public record ConflictRequest(string fileName, string hash);

		[HttpPost("conflict-check")]
		public async Task<IActionResult> ConflictCheck([FromBody] ConflictRequest req)
		{
			try
			{
				var shortUserId = GetRequiredShortUserId();
				var res = await _uploadManager.CheckConflictAsync(shortUserId, req.fileName, req.hash);
				return Ok(res);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Unauthorized(ex.Message);
			}
		}

		[HttpPost("complete")]
		public async Task<IActionResult> Complete([FromForm] string hash)
		{
			try
			{
				var shortUserId = GetRequiredShortUserId();
				var result = await _uploadManager.CompleteSessionAsync(shortUserId, hash);
				if (!result.Success) return BadRequest(new { status = "error", message = result.Message });
				return Ok(new { status = "ok" });
			}
			catch (UnauthorizedAccessException ex)
			{
				return Unauthorized(ex.Message);
			}
		}

        [HttpGet("download/{fileGroupId}/{version}")]
        public async Task<IActionResult> DownloadFile(Guid fileGroupId, int version)
        {
            try
            {
                var shortUserId = GetRequiredShortUserId();
                var file = await _uploadManager.GetFileVersionAsync(shortUserId, fileGroupId, version);
                Console.WriteLine($"Попытка скачать: user={shortUserId}, groupId={fileGroupId}, version={version}");

                if (file == null)
                    return NotFound();

                return File(file.Content, "application/octet-stream", file.FileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        }


        [HttpDelete("delete/{fileGroupId}/{version}")]
        public async Task<IActionResult> DeleteFile(Guid fileGroupId, int version)
        {
            Console.WriteLine($"[DELETE] fileGroupId: {fileGroupId}, version: {version}");

            try
            {
                var shortUserId = GetRequiredShortUserId();
                var result = await _uploadManager.DeleteFileVersionAsync(shortUserId, fileGroupId, version);

                if (!result)
                    return BadRequest("Не удалось удалить файл");

                return Ok(new { status = "deleted" });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(ex.Message);
            }
        }

        [HttpGet("user-files")]
        public async Task<IActionResult> GetUserFiles()
        {
            var shortUserId = GetRequiredShortUserId();

            var files = await _uploadManager.GetAllUserFilesAsync(shortUserId);
            var (usedBytes, tempBytes, limitBytes) = await _uploadManager.GetStorageStatsAsync(shortUserId);

            return Ok(new
            {
                files,
                usedBytes,
                tempBytes,
                limitBytes
            });
        }

        [HttpGet("versions/{fileName}")]
		public async Task<IActionResult> GetFileVersions(string fileName)
		{
			try
			{
				var shortUserId = GetRequiredShortUserId();
				var versions = await _uploadManager.GetAllVersionsAsync(shortUserId, fileName);
				return Ok(versions);
			}
			catch (UnauthorizedAccessException ex)
			{
				return Unauthorized(ex.Message);
			}
		}

	}
}
