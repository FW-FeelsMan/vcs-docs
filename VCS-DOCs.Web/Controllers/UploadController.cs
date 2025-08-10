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
        private readonly ISharedLinkService _sharedLinks;

        public UploadController(
            UploadManager uploadManager,
            IUserInfoProvider userInfoProvider,
            IConfiguration cfg,
            ISharedLinkService sharedLinks)
        {
            _uploadManager = uploadManager;
            _userInfoProvider = userInfoProvider;
            _cfg = cfg;
            _sharedLinks = sharedLinks;
        }

        // ====== ACTIVE STATE ======
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

        // ====== LIST / STATS ======
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

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var (used, temp, limit) = await _uploadManager.GetStorageStatsAsync(shortUserId, ct);
            return Ok(new { usedBytes = used, tempBytes = temp, limitBytes = limit });
        }

        // ====== STATUS / RESTART ======
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

        // ====== CHUNK UPLOAD ======
        [HttpPost("chunk")]
        [RequestSizeLimit(long.MaxValue)]
        public async Task<IActionResult> UploadChunk(
            [FromForm] IFormFile chunk,
            [FromForm] string hash,
            [FromForm] int chunkIndex,
            [FromForm] int totalChunks,
            [FromForm] long fileSize,
            [FromForm] string fileName,
            [FromForm] int? targetVersion,
            CancellationToken ct)
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

            // IMPORTANT: pass targetVersion BEFORE ct to match manager signature
            var r = await _uploadManager.HandleChunkUploadAsync(
                shortUserId,
                chunk,
                hash,
                chunkIndex,
                totalChunks,
                fileSize,
                fileName,
                targetVersion,
                ct
            );

            if (!r.ok)
            {
                if (r.message == "insufficient_storage")
                    return StatusCode(507, new { message = "Недостаточно места на диске" });

                if (r.message == "infected")
                    return Conflict(new { message = "infected", nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId });

                if (r.message == "av_timeout" || r.message == "av_unavailable")
                    return StatusCode(503, new { message = r.message });

                return Conflict(new { message = r.message, nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId });
            }

            return Ok(new { nextExpectedIndex = r.nextExpectedIndex, sessionId = r.sessionId, completed = r.nextExpectedIndex == totalChunks });
        }

        // ====== VERSIONS / DOWNLOAD / DELETE ======
        [HttpGet("versions")]
        public async Task<IActionResult> Versions([FromQuery] string fileName, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var versions = await _uploadManager.GetAllVersionsAsync(shortUserId, fileName, ct);
            return Ok(versions);
        }

        [HttpGet("download/{fileGroupId:guid}/{version:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> DownloadFile(Guid fileGroupId, int version, CancellationToken ct)
        {
            var shortUserId = GetRequiredShortUserId();
            var opened = await _uploadManager.OpenFileVersionStreamAsync(shortUserId, fileGroupId, version, ct);
            if (opened == null) return NotFound();
            return File(opened.Stream, "application/octet-stream", opened.FileName, enableRangeProcessing: true);
        }

        [HttpDelete("delete/{fileGroupId:guid}/{version:int}")]
        public async Task<IActionResult> DeleteFile(Guid fileGroupId, int version)
        {
            var shortUserId = GetRequiredShortUserId();
            var result = await _uploadManager.DeleteFileVersionAsync(shortUserId, fileGroupId, version);
            if (!result) return BadRequest("Не удалось удалить файл");
            return Ok(new { status = "deleted" });
        }

        // ====== SHARING: Legacy HMAC ======
        [HttpPost("share-link")]
        public async Task<IActionResult> CreateShareLink([FromForm] Guid fileGroupId, [FromForm] int version, [FromForm] int ttlHours = 168, CancellationToken ct = default)
        {
            if (version <= 0) return BadRequest("invalid version");
            if (ttlHours <= 0 || ttlHours > 24 * 30) ttlHours = 168;

            var shortUserId = GetRequiredShortUserId();

            var opened = await _uploadManager.OpenFileVersionStreamAsync(shortUserId, fileGroupId, version, ct);
            if (opened == null) return NotFound();
            await opened.Stream.DisposeAsync();

            var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttlHours * 3600L;
            var token = BuildSignature(fileGroupId, version, exp);

            var origin = $"{Request.Scheme}://{Request.Host}";
            var url = $"{origin}/api/Upload/public?g={fileGroupId:N}&v={version}&exp={exp}&sig={token}";
            return Ok(new { url, expiresAt = exp });
        }

        [AllowAnonymous]
        [HttpGet("public")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> PublicDownload([FromQuery] Guid g, [FromQuery] int v, [FromQuery] long exp, [FromQuery] string sig, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(sig) || v <= 0) return NotFound();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return NotFound();

            var expected = BuildSignature(g, v, exp);
            if (!TimeSafeEquals(expected, sig)) return NotFound();

            var found = await _uploadManager.FindAnyCompletedByGroupVersionAsync(g, v, ct);
            if (found == null) return NotFound();

            var opened = await _uploadManager.OpenFileVersionStreamAsync(found.Value.ownerShort, g, v, ct);
            if (opened == null) return NotFound();

            return File(opened.Stream, "application/octet-stream", opened.FileName, enableRangeProcessing: true);
        }

        // ====== SHARING: DB-backed links ======
        [HttpPost("share-db")]
        public async Task<IActionResult> CreateShareDb([FromForm] Guid fileGroupId, [FromForm] int version, [FromForm] int ttlHours = 168,
                                                       [FromForm] int? maxDownloads = null, [FromForm] bool requireAuth = false, CancellationToken ct = default)
        {
            if (version <= 0) return BadRequest("invalid version");
            if (ttlHours <= 0 || ttlHours > 24 * 30) ttlHours = 168;

            var shortUserId = GetRequiredShortUserId();
            var opened = await _uploadManager.OpenFileVersionStreamAsync(shortUserId, fileGroupId, version, ct);
            if (opened == null) return NotFound();
            await opened.Stream.DisposeAsync();

            var link = await _sharedLinks.CreateAsync(shortUserId, fileGroupId, version, ttlHours, maxDownloads, requireAuth, ct);

            var origin = $"{Request.Scheme}://{Request.Host}";
            var url = $"{origin}/api/Upload/public/{link.Id:D}";
            return Ok(new
            {
                id = link.Id,
                url,
                expiresAt = link.Exp,
                maxDownloads = link.MaxDownloads,
                requireAuth = link.RequireAuth
            });
        }

        [AllowAnonymous]
        [HttpGet("public/{id:guid}")]
        [Produces("application/octet-stream")]
        public async Task<IActionResult> PublicDbDownload([FromRoute] Guid id, CancellationToken ct = default)
        {
            var shortId = GetRequiredShortUserIdSafe(); // null if anonymous
            var link = await _sharedLinks.GetAsync(id, ct);
            if (link == null) return NotFound();

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (link.Exp <= now) return NotFound();

            if (link.RequireAuth && string.IsNullOrWhiteSpace(shortId))
            {
                return Unauthorized();
            }

            var found = await _uploadManager.FindAnyCompletedByGroupVersionAsync(link.FileGroupId, link.Version, ct);
            if (found == null) return NotFound();

            var consumed = await _sharedLinks.TryConsumeAsync(id, ct);
            if (consumed.link == null) return NotFound();

            var opened = await _uploadManager.OpenFileVersionStreamAsync(found.Value.ownerShort, link.FileGroupId, link.Version, ct);
            if (opened == null) return NotFound();

            return File(opened.Stream, "application/octet-stream", opened.FileName, enableRangeProcessing: true);
        }

        // ====== Helpers ======
        private string? GetRequiredShortUserIdSafe()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) return null;
            return userId.Replace("-", "").Substring(0, 8);
        }

        private string GetRequiredShortUserId()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("no user");
            return userId.Replace("-", "").Substring(0, 8);
        }

        private string BuildSignature(Guid groupId, int version, long exp)
        {
            var secret = _cfg["ShareLinks:Secret"];
            if (string.IsNullOrEmpty(secret))
                secret = "CHANGE_ME_SUPER_SECRET_256bit_key_minimum";

            var payload = $"{groupId:N}.{version}.{exp}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static bool TimeSafeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}