using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Upload.Core;

namespace VCS_DOCs.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private readonly UploadManager _uploadManager;
        private readonly IUserInfoProvider _userInfoProvider;

        public UploadController(UploadManager uploadManager, IUserInfoProvider userInfoProvider)
        {
            _uploadManager = uploadManager;
            _userInfoProvider = userInfoProvider;
        }

        [HttpGet("active")]
        public async Task<IActionResult> Active(CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var info = await _uploadManager.GetActiveUploadForUserAsync(shortUserId, ct);
            if (info == null) return Ok(new { found = false });
            return Ok(new
            {
                found = true,
                sessionId = info.SessionId,
                fileGroupId = info.FileGroupId,
                fileName = info.FileName,
                fileHash = info.FileHash,
                version = info.Version,
                fileSize = info.FileSize,
                updatedAt = info.UpdatedAt,
                uploaded = info.Uploaded
            });
        }

        public class CleanupRequest
        {
            [JsonPropertyName("hash")]
            public string Hash { get; set; } = "";
        }

        [HttpPost("cleanup-temp")]
        public async Task<IActionResult> CleanupTemp([FromBody] CleanupRequest body, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            await _uploadManager.CleanupTempByHashAsync(shortUserId, body.Hash ?? "", ct);
            return Ok(new { ok = true });
        }

        [HttpGet("user-files")]
        public async Task<IActionResult> GetUserFiles(CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var files = await _uploadManager.GetAllUserFilesAsync(shortUserId, ct);
            var stats = await _uploadManager.GetStorageStatsAsync(shortUserId, ct);
            return Ok(new
            {
                files,
                usedBytes = stats.usedBytes,
                tempBytes = stats.tempBytes,
                limitBytes = stats.limitBytes
            });
        }

        [HttpPost("check-version-conflict")]
        public async Task<IActionResult> CheckVersionConflict([FromForm] string fileName, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var userId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var conflict = await _uploadManager.HasCompletedVersionAsync(userId, fileName, ct);
            return Ok(new { conflict });
        }

        [HttpGet("upload-status")]
        public async Task<IActionResult> UploadStatus([FromQuery] string fileHash, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var r = await _uploadManager.GetOngoingUploadAsync(shortUserId, fileHash, ct);
            if (r.SessionId == Guid.Empty) return Ok(new { found = false });
            return Ok(new { found = true, sessionId = r.SessionId, uploaded = r.Uploaded });
        }

        [HttpPost("restart")]
        public async Task<IActionResult> Restart([FromForm] string fileName, [FromForm] string fileHash, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            await _uploadManager.RestartUploadAsync(shortUserId, fileName, fileHash, ct);
            return Ok(new { restarted = true });
        }

        [HttpPost("chunk")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> UploadChunk([FromForm] IFormFile chunk, [FromForm] string hash, [FromForm] int chunkIndex, [FromForm] int totalChunks, [FromForm] long fileSize, [FromForm] string fileName, CancellationToken ct)
        {
            if (chunk == null || chunk.Length == 0) return BadRequest("empty chunk");
            var shortUserId = GetRequiredShortUserId();
            if (await _uploadManager.GetActiveUploadForUserAsync(shortUserId, ct) is { } act && !string.Equals(act.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { status = "busy", message = "Идёт другая загрузка. Дождитесь окончания или нажмите «Заново» в текущей." });
            }
            var r = await _uploadManager.HandleChunkUploadAsync(shortUserId, chunk, hash, chunkIndex, totalChunks, fileSize, fileName, ct);
            if (!r.ok) return Conflict(new { message = r.message, nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId });
            return Ok(new { nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId, completed = r.nextExpectedIndex == totalChunks });
        }

        [HttpGet("versions")]
        public async Task<IActionResult> Versions([FromQuery] string fileName, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var versions = await _uploadManager.GetAllVersionsAsync(shortUserId, fileName, ct);
            return Ok(versions);
        }

        [HttpGet("download/{fileGroupId:guid}/{version:int}")]
        public async Task<IActionResult> DownloadFile(Guid fileGroupId, int version)
        {
            var shortUserId = GetRequiredShortUserId();
            var file = await _uploadManager.GetFileVersionAsync(shortUserId, fileGroupId, version);
            if (file == null) return NotFound();
            return File(file.Content, "application/octet-stream", file.FileName);
        }

        [HttpDelete("delete/{fileGroupId:guid}/{version:int}")]
        public async Task<IActionResult> DeleteFile(Guid fileGroupId, int version)
        {
            var shortUserId = GetRequiredShortUserId();
            var result = await _uploadManager.DeleteFileVersionAsync(shortUserId, fileGroupId, version);
            if (!result) return BadRequest("Не удалось удалить файл");
            return Ok(new { status = "deleted" });
        }

        private string GetRequiredShortUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("no user");
            return userId.Replace("-", "").Substring(0, 8);
        }
    }
}