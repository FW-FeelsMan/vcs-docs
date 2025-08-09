using System;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Upload.Core;

namespace VCS_DOCs.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private const int FreshSeconds = 60;
        private readonly UploadManager _uploadManager;
        private readonly IUserInfoProvider _userInfoProvider;
        private readonly IConfiguration _cfg;

        public UploadController(UploadManager uploadManager, IUserInfoProvider userInfoProvider, IConfiguration cfg)
        {
            _uploadManager = uploadManager;
            _userInfoProvider = userInfoProvider;
            _cfg = cfg;
        }

        [HttpGet("active")]
        public async Task<IActionResult> Active(CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var info = await _uploadManager.GetActiveUploadForUserAsync(shortUserId, ct);
            if (info == null) return Ok(new { found = false });

            var ageSec = (int)Math.Max(0, Math.Floor((DateTimeOffset.UtcNow - info.UpdatedAt).TotalSeconds));
            var isFresh = ageSec <= FreshSeconds && !info.Stopped;

            return Ok(new
            {
                found = true,
                isFresh,
                ageSec,
                stopped = info.Stopped,
                sessionId = info.SessionId,
                fileGroupId = info.FileGroupId,
                fileName = info.FileName,
                fileHash = info.FileHash,
                version = info.Version,
                fileSize = info.FileSize,
                uploaded = info.Uploaded,
                uploadedBytes = info.UploadedBytes,
                updatedAt = info.UpdatedAt
            });
        }

        [HttpPost("heartbeat")]
        public async Task<IActionResult> Heartbeat([FromForm] string fileHash, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            await _uploadManager.TouchActiveAsync(shortUserId, fileHash, ct);
            return Ok(new { ok = true });
        }

        [HttpPost("stopped")]
        public async Task<IActionResult> Stopped([FromForm] string fileHash, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            await _uploadManager.MarkStoppedAsync(shortUserId, fileHash, ct);
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

        [HttpGet("upload-status")]
        public async Task<IActionResult> UploadStatus([FromQuery] string fileHash, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var r = await _uploadManager.GetOngoingUploadAsync(shortUserId, fileHash, ct);
            if (r.SessionId == Guid.Empty) return Ok(new { found = false });
            return Ok(new { found = true, sessionId = r.SessionId, uploaded = r.Uploaded });
        }

        [HttpPost("check-version-conflict")]
        public async Task<IActionResult> CheckVersionConflict([FromForm] string fileName, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var fullUserId = _userInfoProvider.ResolveFullUserId(shortUserId);
            var conflict = await _uploadManager.HasCompletedVersionAsync(fullUserId, fileName, ct);
            return Ok(new { conflict });
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

            var act = await _uploadManager.GetActiveUploadForUserAsync(shortUserId, ct);
            if (act != null && !string.Equals(act.FileHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                var ageSec = (int)Math.Max(0, Math.Floor((DateTimeOffset.UtcNow - act.UpdatedAt).TotalSeconds));
                if (ageSec <= FreshSeconds && !act.Stopped)
                    return Conflict(new { status = "busy", message = "Идёт другая загрузка. Дождитесь окончания или нажмите «Заново» в текущей." });
            }

            var r = await _uploadManager.HandleChunkUploadAsync(shortUserId, chunk, hash, chunkIndex, totalChunks, fileSize, fileName, ct);

            if (!r.ok)
            {
                if (r.message == "insufficient_storage") return StatusCode(507, new { message = "Недостаточно места на диске" });
                return Conflict(new { message = r.message, nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId });
            }

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

        // ----------------------
        //  SHARING (signed link)
        // ----------------------

        // Creates a time-limited signed link that can be opened by anyone who has the URL
        // No DB migrations required.
        [HttpPost("share-link")]
        public async Task<IActionResult> CreateShareLink([FromForm] Guid fileGroupId, [FromForm] int version, [FromForm] int ttlHours = 168, CancellationToken ct = default)
        {
            if (version <= 0) return BadRequest("invalid version");
            if (ttlHours <= 0 || ttlHours > 24 * 30) ttlHours = 168; // clamp

            var shortUserId = GetRequiredShortUserId();
            // verify ownership exists
            var session = await _uploadManager.FindCompletedByOwnerAsync(shortUserId, fileGroupId, version, ct);
            if (session == null) return NotFound();

            var exp = DateTimeOffset.UtcNow.AddHours(ttlHours).ToUnixTimeSeconds();
            var token = BuildSignature(fileGroupId, version, exp);

            var origin = $"{Request.Scheme}://{Request.Host}";
            var url = $"{origin}/api/Upload/public?g={fileGroupId:N}&v={version}&exp={exp}&sig={token}";
            return Ok(new { url, expiresAt = exp });
        }

        // Public download via signed link
        [AllowAnonymous]
        [HttpGet("public")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> PublicDownload([FromQuery] Guid g, [FromQuery] int v, [FromQuery] long exp, [FromQuery] string sig, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sig) || v <= 0) return NotFound();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return NotFound();

            var expected = BuildSignature(g, v, exp);
            if (!TimeSafeEquals(expected, sig)) return NotFound();

            // find file session by fileGroupId+version regardless of user
            var session = await _uploadManager.FindAnyCompletedByGroupVersionAsync(g, v, ct);
            if (session == null) return NotFound();

            var ownerShort = session.UserId.Replace("-", "").Substring(0, 8);
            var file = await _uploadManager.GetFileVersionAsync(ownerShort, g, v);
            if (file == null) return NotFound();

            return File(file.Content, "application/octet-stream", file.FileName);
        }

        private string BuildSignature(Guid groupId, int version, long exp)
        {
            var secret = _cfg["ShareLinks:Secret"];
            if (string.IsNullOrEmpty(secret))
            {
                // fallback dev secret; set ShareLinks:Secret in appsettings
                secret = "CHANGE_ME_SUPER_SECRET_256bit_key_minimum";
            }
            var payload = $"{groupId:N}.{version}.{exp}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return ToBase64Url(hash);
        }

        private static bool TimeSafeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static string ToBase64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private string GetRequiredShortUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("no user");
            return userId.Replace("-", "").Substring(0, 8);
        }
    }
}