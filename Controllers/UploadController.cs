using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using VCS_DOCs.Configuration;

namespace VCS_DOCs.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class UploadController : ControllerBase
	{
		private readonly ILogger<UploadController> _logger;
		private readonly ApplicationDbContext _db;
		private readonly UserDataPathOptions _options;

		public UploadController(
			ILogger<UploadController> logger,
			ApplicationDbContext db,
			IOptions<UserDataPathOptions> options)
		{
			_logger = logger;
			_db = db;
			_options = options.Value;
		}

		private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
		private string GetShortId(string fullId) => fullId.Replace("-", "").Substring(0, 8);

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
			if (chunk == null || chunk.Length == 0)
				return BadRequest("Файл не получен");

			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized("Пользователь не найден");

			var shortId = GetShortId(userId);
			var chunkDir = Path.Combine(_options.BasePath, $"u_{shortId}", "temp", hash);
			Directory.CreateDirectory(chunkDir);

			var session = await _db.FileUploadSessions
				.Include(s => s.Chunks)
				.FirstOrDefaultAsync(s =>
					s.UserId == userId &&
					s.FileHash == hash &&
					s.Status == "incomplete");

			if (session == null)
			{
				var lastComplete = await _db.FileUploadSessions
					.Where(x => x.UserId == userId && x.OriginalFileName == fileName && x.Status == "complete")
					.OrderByDescending(x => x.Version)
					.FirstOrDefaultAsync();

				int newVersion = lastComplete != null ? lastComplete.Version + 1 : 1;

				Guid fileId;

				if (replaceVersion.HasValue)
				{
					var replacing = await _db.FileUploadSessions
						.Include(x => x.Chunks)
						.FirstOrDefaultAsync(x =>
							x.UserId == userId &&
							x.OriginalFileName == fileName &&
							x.Version == replaceVersion.Value &&
							x.Status == "complete");

					if (replacing != null)
					{
						_db.FileUploadChunks.RemoveRange(replacing.Chunks);
						_db.FileUploadSessions.Remove(replacing);
						await _db.SaveChangesAsync(); // Устраняем конфликты

						fileId = replacing.FileId;
					}
					else
					{
						fileId = Guid.NewGuid();
					}
				}
				else
				{
					fileId = lastComplete?.FileId ?? Guid.NewGuid();
				}

				session = new FileUploadSession
				{
					UserId = userId,
					FileId = fileId,
					OriginalFileName = fileName,
					FileHash = hash,
					FileSize = fileSize,
					TotalChunks = totalChunks,
					Version = replaceVersion ?? newVersion,
					Status = "incomplete",
					CreatedAt = DateTime.Now,
					UpdatedAt = DateTime.Now
				};

				for (int i = 0; i < totalChunks; i++)
					session.Chunks.Add(new FileUploadChunk { Index = i });

				_db.FileUploadSessions.Add(session);
				await _db.SaveChangesAsync();
			}

			var chunkEntry = session.Chunks.FirstOrDefault(c => c.Index == chunkIndex);
			if (chunkEntry == null)
				return BadRequest("Некорректный индекс чанка");

			var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex:D4}");

			try
			{
				await using var stream = new FileStream(chunkPath, FileMode.Create);
				await chunk.CopyToAsync(stream);
			}
			catch (DirectoryNotFoundException ex)
			{
				_logger.LogWarning("Директория для чанка отсутствует: {Path}", chunkPath);
				return BadRequest($"Папка для хранения чанков отсутствует. Возможно, загрузка была сброшена.");
			}

			chunkEntry.Uploaded = true;
			chunkEntry.UpdatedAt = DateTime.Now;
			session.UpdatedAt = DateTime.Now;

			await _db.SaveChangesAsync();
			return Ok(new { message = "Чанк сохранён", chunkIndex });
		}

		[HttpPost("complete")]
		public async Task<IActionResult> CompleteUpload([FromForm] string hash)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized("Пользователь не найден");

			var session = await _db.FileUploadSessions
				.Include(s => s.Chunks)
				.FirstOrDefaultAsync(s => s.UserId == userId && s.FileHash == hash && s.Status == "incomplete");

			if (session == null || session.Chunks.Any(c => !c.Uploaded))
				return BadRequest("Ошибка сессии или не все чанки загружены");

			var shortId = GetShortId(userId);
			var newVersion = session.Version;

			string basePath = Path.Combine(_options.BasePath, $"u_{shortId}");
			string tempDir = Path.Combine(basePath, "temp", hash);
			string filesDir = Path.Combine(basePath, "files", session.FileId.ToString(), $"v{newVersion}");
			Directory.CreateDirectory(filesDir);

			string fileBase = Path.GetFileNameWithoutExtension(session.OriginalFileName);
			string fileExt = Path.GetExtension(session.OriginalFileName);
			string versionedName = $"{fileBase}{fileExt}";
			string finalPath = Path.Combine(filesDir, versionedName);

			try
			{
				using (var output = new FileStream(finalPath, FileMode.Create))
				{
					for (int i = 0; i < session.TotalChunks; i++)
					{
						var chunkPath = Path.Combine(tempDir, $"chunk_{i:D4}");
						if (!System.IO.File.Exists(chunkPath))
						{
							_logger.LogWarning("Не найден чанк {Index} для сборки: {Path}", i, chunkPath);
							return BadRequest($"Чанк {i} отсутствует. Загрузка нарушена или прервана.");
						}

						using var input = new FileStream(chunkPath, FileMode.Open);
						await input.CopyToAsync(output);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка при сборке файла");
				return StatusCode(500, "Ошибка сборки файла");
			}

			session.Status = "complete";
			session.IsLatest = true;
			session.UpdatedAt = DateTime.Now;

			await _db.FileUploadSessions
				.Where(x => x.UserId == userId && x.FileId == session.FileId && x.Id != session.Id)
				.ForEachAsync(x => x.IsLatest = false);

			_db.FileUploadChunks.RemoveRange(session.Chunks);

			await _db.SaveChangesAsync();
			DeleteDirectorySafe(tempDir);

			return Ok(new { message = "Файл собран", FileName = versionedName, Version = newVersion });
		}

		private void DeleteDirectorySafe(string path)
		{
			try
			{
				if (Directory.Exists(path))
					Directory.Delete(path, true);
			}
			catch (Exception ex)
			{
				_logger.LogWarning("Ошибка при удалении папки {Path}: {Error}", path, ex.Message);
			}
		}

		[HttpPost("conflict-check")]
		public async Task<IActionResult> CheckConflict([FromBody] ConflictCheckRequest req)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized(new { status = "unauthorized" });

			var latest = await _db.FileUploadSessions
				.Where(x => x.UserId == userId && x.OriginalFileName == req.FileName)
				.OrderByDescending(x => x.UpdatedAt)
				.FirstOrDefaultAsync();

			if (latest == null)
				return Ok(new { status = "ok" });

			if (latest.Status != "complete")
				return Ok(new { status = "uploading" });

			return Ok(new { status = "exists", replaceVersion = latest.Version });
		}

		[HttpGet("versions/{fileName}")]
		public async Task<IActionResult> GetFileVersions(string fileName)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			var lastSession = await _db.FileUploadSessions
				.Where(x => x.UserId == userId && x.OriginalFileName == fileName && x.Status == "complete")
				.OrderByDescending(x => x.Version)
				.FirstOrDefaultAsync();

			if (lastSession == null)
				return Ok(new List<object>());

			var fileId = lastSession.FileId;

			var versions = await _db.FileUploadSessions
				.Where(x => x.UserId == userId && x.FileId == fileId && x.Status == "complete")
				.OrderByDescending(x => x.Version)
				.Select(x => new { x.Version, x.UpdatedAt })
				.ToListAsync();

			return Ok(versions);
		}

		[HttpPost("cleanup-incomplete")]
		public async Task<IActionResult> CleanupIncomplete()
		{
			var staleTime = DateTime.Now.AddHours(-1);
			var staleSessions = await _db.FileUploadSessions
				.Include(x => x.Chunks)
				.Where(x => x.Status == "incomplete" && x.UpdatedAt < staleTime)
				.ToListAsync();

			foreach (var session in staleSessions)
			{
				var tempDir = Path.Combine(_options.BasePath, $"u_{GetShortId(session.UserId)}", "temp", session.FileHash);
				DeleteDirectorySafe(tempDir);

				_db.FileUploadChunks.RemoveRange(session.Chunks);
				_db.FileUploadSessions.Remove(session);
			}

			await _db.SaveChangesAsync();

			return Ok(new { removed = staleSessions.Count });
		}

		[HttpGet("upload-status")]
		public async Task<IActionResult> GetUploadStatus(string fileHash)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			var session = await _db.FileUploadSessions
				.Include(s => s.Chunks)
				.Where(s => s.UserId == userId && s.FileHash == fileHash && s.Status == "incomplete")
				.OrderByDescending(s => s.UpdatedAt)
				.FirstOrDefaultAsync();

			if (session == null)
				return Ok(new { found = false });

			var uploadedChunks = session.Chunks
				.Where(c => c.Uploaded)
				.Select(c => c.Index)
				.ToList();

			return Ok(new
			{
				found = true,
				sessionId = session.Id,
				totalChunks = session.TotalChunks,
				uploaded = uploadedChunks
			});
		}
	}

	public class ConflictCheckRequest
	{
		public string FileName { get; set; } = "";
		public string Hash { get; set; } = "";
	}
}