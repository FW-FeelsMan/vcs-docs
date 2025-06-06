using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VCS_DOCs.Configuration;
using VCS_DOCs.Hubs;

namespace VCS_DOCs.Controllers
{
	[ApiController]
	[Route("api/[controller]")]
	public class UploadController : ControllerBase
	{
		private readonly ILogger<UploadController> _logger;
		private readonly ApplicationDbContext _db;
		private readonly UserDataPathOptions _options;
		private readonly IHubContext<TaskHub> _hub;

		public UploadController(
			ILogger<UploadController> logger,
			ApplicationDbContext db,
			IOptions<UserDataPathOptions> options,
			IHubContext<TaskHub> hub)
		{
			_logger = logger;
			_db = db;
			_options = options.Value;
			_hub = hub;
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
				// Ищем любую существующую сессию с таким же именем файла
				var existingFile = await _db.FileUploadSessions
					.Where(x => x.UserId == userId && x.OriginalFileName == fileName && x.Status == "complete")
					.OrderByDescending(x => x.Version)
					.FirstOrDefaultAsync();

				// Используем существующий FileId, если файл с таким именем уже существует
				// Это исправление обеспечивает связь между версиями одного файла
				Guid fileId;
				int newVersion;

				if (existingFile != null)
				{
					// Используем существующий FileId для сохранения связи между версиями
					fileId = existingFile.FileId;
					newVersion = existingFile.Version + 1;
				}
				else
				{
					// Только для новых файлов создаем новый FileId
					fileId = Guid.NewGuid();
					newVersion = 1;
				}

				// Если указана конкретная версия для замены
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
						await _db.SaveChangesAsync();
						fileId = replacing.FileId; // Используем FileId заменяемой версии
						newVersion = replaceVersion.Value; // Сохраняем номер версии
					}
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
				return BadRequest("Папка для хранения чанков отсутствует. Возможно, загрузка была сброшена.");
			}

			chunkEntry.Uploaded = true;
			chunkEntry.UpdatedAt = DateTime.Now;
			session.UpdatedAt = DateTime.Now;
			await _db.SaveChangesAsync();
			_logger.LogInformation("[{Time}] Push TaskUpdate (chunk upload): {Title}", DateTime.Now, $"Загрузка файла: {fileName}");
			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = "upload_" + hash,
				title = $"Загрузка файла: {fileName}",
				type = "upload",
				statusClass = "in-progress",
				statusText = $"{session.Chunks.Count(c => c.Uploaded)} / {session.TotalChunks} чанков",
				cancelable = false
			});

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
			_logger.LogInformation("[{Time}] Push TaskUpdate (start compiling): {Title}", DateTime.Now, $"Сборка файла: {session.OriginalFileName}");
			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = $"compiling_{hash}",
				title = $"Сборка файла: {session.OriginalFileName}",
				type = "upload",
				statusClass = "in-progress",
				statusText = "Сборка из чанков...",
				cancelable = false
			});

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
				using var output = new FileStream(finalPath, FileMode.Create);
				for (int i = 0; i < session.TotalChunks; i++)
				{
					var chunkPath = Path.Combine(tempDir, $"chunk_{i:D4}");
					if (!System.IO.File.Exists(chunkPath))
					{
						_logger.LogInformation("[{Time}] Push TaskUpdate (start compiling): {Title}", DateTime.Now, $"Сборка файла: {session.OriginalFileName}");

						await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
						{
							taskKey = $"compiling_{hash}",
							title = $"Сборка файла: {session.OriginalFileName}",
							type = "upload",
							statusClass = "error",
							statusText = $"Ошибка: отсутствует чанк {i}",
							cancelable = false
						});
						return BadRequest($"Чанк {i} отсутствует");
					}

					using var input = new FileStream(chunkPath, FileMode.Open);
					await input.CopyToAsync(output);
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Ошибка при сборке");
				await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
				{
					taskKey = $"compiling_{hash}",
					title = $"Сборка файла: {session.OriginalFileName}",
					type = "upload",
					statusClass = "error",
					statusText = "Ошибка при сборке",
					cancelable = false
				});
				return StatusCode(500, "Ошибка при сборке файла");
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
			_logger.LogInformation("[{Time}] Push TaskUpdate (start compiling): {Title}", DateTime.Now, $"Сборка файла: {session.OriginalFileName}");
			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = $"compiling_{hash}",
				title = $"Сборка файла: {session.OriginalFileName}",
				type = "upload",
				statusClass = "done",
				statusText = "Сборка завершена",
				cancelable = false
			});

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

			// Получаем все версии файла для отображения в выпадающем списке
			var allVersions = await _db.FileUploadSessions
				.Where(x => x.UserId == userId && x.FileId == latest.FileId && x.Status == "complete")
				.OrderByDescending(x => x.Version)
				.Select(x => new { x.Version, x.UpdatedAt })
				.ToListAsync();

			return Ok(new
			{
				status = "exists",
				replaceVersion = latest.Version,
				allVersions = allVersions // Добавляем все версии в ответ
			});
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
		[HttpGet("list")]
		public async Task<IActionResult> ListFiles()
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			// Получаем последние версии файлов
			var latestSessions = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && s.Status == "complete" && s.IsLatest)
				.OrderByDescending(s => s.UpdatedAt)
				.ToListAsync();

			// Получаем все версии для этих файлов
			var fileIds = latestSessions.Select(s => s.FileId).Distinct().ToList();

			var allVersions = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && fileIds.Contains(s.FileId) && s.Status == "complete")
				.ToListAsync();

			var result = latestSessions.Select(latest => new
			{
				FileName = latest.OriginalFileName,
				FileId = latest.FileId,
				FileSize = latest.FileSize,
				UpdatedAt = latest.UpdatedAt,
				LatestVersion = latest.Version,
				Versions = allVersions
					.Where(v => v.FileId == latest.FileId)
					.Select(v => new { v.Version, v.UpdatedAt })
					.OrderByDescending(v => v.Version)
					.ToList()
			});

			return Ok(result);
		}
	}

	public class ConflictCheckRequest
	{
		public string FileName { get; set; } = "";
		public string Hash { get; set; } = "";
	}
}

