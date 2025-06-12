using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using VCS_DOCs.Configuration;
using VCS_DOCs.Hubs;
using Microsoft.AspNetCore.StaticFiles;
using System.Text.RegularExpressions;

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
			fileName = SafeFileName(fileName);

			if (chunk == null || chunk.Length == 0)
				return BadRequest("Файл не получен");

			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized("Пользователь не найден");

			string extension = Path.GetExtension(fileName).ToLowerInvariant();
			string[] forbidden = [
				".exe", ".bat", ".cmd", ".js", ".vbs", ".scr", ".ps1",
				".msi", ".hta", ".jar", ".dll", ".com", ".pif", ".cpl"
			];

			if (chunk.ContentType.StartsWith("application/x-msdownload"))
			{
				return BadRequest("Этот файл выглядит как исполняемый. Запрещено загружать исполняемые файлы.");
			}
			if (forbidden.Contains(extension))
			{
				_logger.LogWarning("Пользователь {UserId} попытался загрузить запрещённый файл: {FileName}", userId, fileName);

				await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
				{
					taskKey = "upload_" + hash,
					title = $"Загрузка файла: {fileName}",
					type = "upload",
					statusClass = "error",
					statusText = $"Файл не разрешён к загрузке ({extension})",
					cancelable = false
				});
				await _hub.Clients.User(userId).SendAsync("TaskComplete", new
				{
					taskKey = "upload_" + hash,
					removeAfter = 5000
				});

				return BadRequest($"Файлы с расширением {extension} не разрешены к загрузке.");
			}

			var otherUpload = await _db.FileUploadSessions
				.AnyAsync(s => s.UserId == userId && s.Status == "incomplete" && s.FileHash != hash);

			if (otherUpload)
			{
				return Conflict(new { status = "busy", message = "У вас уже идёт загрузка/подготовка к загрузке другого файла." });
			}

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
				var existingFile = await _db.FileUploadSessions
					.Where(x => x.UserId == userId && x.OriginalFileName == fileName && x.Status == "complete")
					.OrderByDescending(x => x.Version)
					.FirstOrDefaultAsync();

				Guid fileId;
				int newVersion;

				if (existingFile != null)
				{
					fileId = existingFile.FileId;
					newVersion = existingFile.Version + 1;
				}
				else
				{
					fileId = Guid.NewGuid();
					newVersion = 1;
				}

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
						await _hub.Clients.User(userId).SendAsync("StopUpload", hash);

						try
						{
							await _db.SaveChangesAsync();
						}
						catch (DbUpdateConcurrencyException ex)
						{
							_logger.LogWarning("Upload canceled: Session removed for user {UserId}, hash: {Hash}. Exception: {Message}", userId, hash, ex.Message);
							return Conflict(new { status = "canceled", message = "Загрузка была отменена." });
						}

						fileId = replacing.FileId;
						newVersion = replaceVersion.Value;
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

				await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
				{
					taskKey = "upload_" + hash,
					title = $"Загрузка файла: {fileName}",
					type = "upload",
					statusClass = "starting",
					statusText = "Подготовка...",
					cancelable = true
				});

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

			try
			{
				await _db.SaveChangesAsync();
			}
			catch (DbUpdateConcurrencyException ex)
			{
				_logger.LogWarning("Chunk not saved: session removed concurrently. {Message}", ex.Message);
				return Conflict("Загрузка была отменена.");
			}

			var uploaded = session.Chunks.Count(c => c.Uploaded);
			var percent = (int)((double)uploaded / session.TotalChunks * 100);

			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = "upload_" + hash,
				title = $"Загрузка файла: {fileName}",
				type = "upload",
				statusClass = "in-progress",
				statusText = $"Загружено: {percent}%",
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

			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = "upload_" + hash,
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
						await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
						{
							taskKey = "upload_" + hash,
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
					taskKey = "upload_" + hash,
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

			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = "upload_" + hash,
				title = $"Сборка файла: {session.OriginalFileName}",
				type = "upload",
				statusClass = "done",
				statusText = "Сборка завершена",
				cancelable = false
			});

			await _hub.Clients.User(userId).SendAsync("TaskComplete", new
			{
				taskKey = "upload_" + hash,
				removeAfter = 5000
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
		[HttpDelete("delete/{fileId}/{version}")]
		public async Task<IActionResult> DeleteVersion(string fileId, int version)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized(new { status = "unauthorized" });

			if (!Guid.TryParse(fileId, out var fileGuid))
				return BadRequest(new { status = "invalid_fileId" });

			// находим запись в БД…
			var session = await _db.FileUploadSessions
				.Include(s => s.Chunks)
				.FirstOrDefaultAsync(s =>
				   s.UserId == userId &&
				   s.FileId == fileGuid &&
				   s.Version == version);

			if (session == null)
				return NotFound(new { status = "not_found" });

			var shortId = GetShortId(userId);
			var basePath = Path.Combine(_options.BasePath, $"u_{shortId}");
			var versionDir = Path.Combine(basePath, "files", fileGuid.ToString(), $"v{version}");

			// 1) Удаляем физически папку версии
			if (Directory.Exists(versionDir))
			{
				try
				{
					Directory.Delete(versionDir, true);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Ошибка удаления каталога версии {Version}", version);
					return StatusCode(500, new { status = "fs_error" });
				}
			}

			// 1.1) Если после этого папка с fileId пустая — удаляем её
			var fileFolder = Path.Combine(basePath, "files", fileGuid.ToString());
			if (Directory.Exists(fileFolder))
			{
				// проверяем, остались ли внутри любые подпапки (v*)
				var hasSubdirs = Directory.EnumerateDirectories(fileFolder).Any();
				if (!hasSubdirs)
				{
					try
					{
						Directory.Delete(fileFolder, false);
					}
					catch (Exception ex)
					{
						_logger.LogWarning(ex, "Не удалось удалить пустую папку файла {FileId}", fileId);
					}
				}
			}

			// 2) Удаляем записи из БД
			_db.FileUploadChunks.RemoveRange(session.Chunks);
			_db.FileUploadSessions.Remove(session);
			await _db.SaveChangesAsync();

			// 3) Обновляем IsLatest у новой версии
			var nextLatest = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && s.FileId == fileGuid && s.Status == "complete")
				.OrderByDescending(s => s.Version)
				.FirstOrDefaultAsync();

			if (nextLatest != null)
			{
				nextLatest.IsLatest = true;
				await _db.SaveChangesAsync();
			}

			return Ok(new { status = "deleted", newLatest = nextLatest?.Version });
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
		private long GetDirectorySize(string path)
		{
			if (!Directory.Exists(path))
				return 0;

			long total = 0;
			foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
			{
				try
				{
					// только если файл действительно существует
					if (System.IO.File.Exists(file))
						total += new FileInfo(file).Length;
				}
				catch (IOException)
				{
					// файл стал недоступен или права, пропускаем
				}
				catch (UnauthorizedAccessException)
				{
					// нет прав — пропускаем
				}
			}
			return total;
		}


		[HttpGet("list")]
		public async Task<IActionResult> ListFiles()
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			// 1) Собираем файлы как раньше
			var latestSessions = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && s.Status == "complete" && s.IsLatest)
				.OrderByDescending(s => s.UpdatedAt)
				.ToListAsync();

			var fileIds = latestSessions.Select(s => s.FileId).Distinct().ToList();
			var allVersions = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && fileIds.Contains(s.FileId) && s.Status == "complete")
				.ToListAsync();

			var files = latestSessions.Select(latest => new
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
			}).ToList();

			// 2) Считаем места на диске
			var shortId = GetShortId(userId);
			var basePath = Path.Combine(_options.BasePath, $"u_{shortId}");
			var filesDir = Path.Combine(basePath, "files");
			var tempDir = Path.Combine(basePath, "temp");
			long usedBytes = GetDirectorySize(filesDir);
			long tempBytes = GetDirectorySize(tempDir);
			long limitBytes = 10L * 1024 * 1024 * 1024; // 10 ГБ

			return Ok(new
			{
				files,
				usedBytes,
				tempBytes,
				limitBytes
			});
		}
		[HttpGet("download/{fileId}/{version}")]
		public async Task<IActionResult> DownloadVersion(string fileId, int version)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();

			if (!Guid.TryParse(fileId, out var fileGuid))
				return BadRequest("Неверный идентификатор файла");

			// Ищем в БД метаданные по сессии конкретной версии
			var session = await _db.FileUploadSessions
				.Where(s => s.UserId == userId && s.FileId == fileGuid && s.Version == version && s.Status == "complete")
				.FirstOrDefaultAsync();

			if (session == null)
				return NotFound("Версия не найдена");

			// Собираем путь к файлу на диске
			var shortId = GetShortId(userId);
			var basePath = Path.Combine(_options.BasePath, $"u_{shortId}", "files", fileGuid.ToString(), $"v{version}");
			var fileName = session.OriginalFileName;
			var filePath = Path.Combine(basePath, fileName);

			if (!System.IO.File.Exists(filePath))
				return NotFound("Файл отсутствует на сервере");

			// Определяем MIME-тип (можете расширить карту по расширениям, если нужно)
			var contentType = "application/octet-stream";
			new FileExtensionContentTypeProvider()
				.TryGetContentType(fileName, out var mappedType);
			if (!string.IsNullOrEmpty(mappedType))
				contentType = mappedType;

			// Возвращаем файл вместе с правильным заголовком для скачивания
			var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			return File(stream, contentType, fileName);
		}
		[HttpPost("cancel")]
		public async Task<IActionResult> CancelUpload([FromBody] CancelUploadRequest req)
		{
			var userId = GetUserId();
			if (string.IsNullOrEmpty(userId))
				return Unauthorized();
			await _hub.Clients.User(userId).SendAsync("StopUpload", req.Hash);
			await Task.Delay(500);
			var session = await _db.FileUploadSessions
				.Include(s => s.Chunks)
				.FirstOrDefaultAsync(s => s.UserId == userId && s.FileHash == req.Hash && s.Status == "incomplete");

			if (session != null)
			{
				_db.FileUploadChunks.RemoveRange(session.Chunks);
				_db.FileUploadSessions.Remove(session);
				await _db.SaveChangesAsync();

				var shortId = GetShortId(userId);
				var tempDir = Path.Combine(_options.BasePath, $"u_{shortId}", "temp", req.Hash);
				DeleteDirectorySafe(tempDir);
			}

			await _hub.Clients.User(userId).SendAsync("TaskUpdate", new
			{
				taskKey = $"upload_{req.Hash}",
				title = $"Загрузка отменена",
				type = "upload",
				statusClass = "canceled",
				statusText = "Загрузка отменена",
				cancelable = false
			});
			await _hub.Clients.User(userId).SendAsync("TaskComplete", new
			{
				taskKey = $"upload_{req.Hash}",
				removeAfter = 5000
			});

			return Ok();
		}
		private string SafeFileName(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName))
				return "unnamed";

			var invalidChars = Path.GetInvalidFileNameChars();
			var cleaned = new string(fileName.Where(ch => !invalidChars.Contains(ch)).ToArray());

			cleaned = cleaned.Trim().TrimStart('.', '/', '\\');

			if (cleaned.Length > 255)
				cleaned = cleaned.Substring(0, 255);

			return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
		}
	}
	public class CancelUploadRequest
	{
		public string Hash { get; set; } = "";
	}
	public class ConflictCheckRequest
	{
		public string FileName { get; set; } = "";
		public string Hash { get; set; } = "";
	}
}

