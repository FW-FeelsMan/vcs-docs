using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Core.Interfaces;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Upload.Core.Models;

namespace VCS_DOCs.Upload.Core;

public class UploadManager
{
	private readonly IUploadDbContext _db;
	private readonly IFileStorageService _storage;

	public UploadManager(IUploadDbContext db, IFileStorageService storage)
	{
		_db = db;
		_storage = storage;
	}

	public async Task<IActionResult> HandleChunkUploadAsync(
	string userId,
	IFormFile chunk,
	string hash,
	int chunkIndex,
	int totalChunks,
	long fileSize,
	int? replaceVersion,
	string fileName)
	{
		if (chunk == null || chunk.Length == 0)
			return new BadRequestObjectResult("Чанк пустой или отсутствует");

		var session = await _db.FileUploadSessions
			.FirstOrDefaultAsync(s => s.UserId == userId && s.FileHash == hash && s.Status != "complete");

		if (session == null)
		{
			session = new FileUploadSessionModel
			{
				FileId = Guid.NewGuid(),
				UserId = userId,
				OriginalFileName = fileName,
				FileHash = hash,
				FileSize = fileSize,
				Status = "uploading",
				UpdatedAt = DateTime.UtcNow,
				Version = replaceVersion ?? 1
			};

			await _db.FileUploadSessions.AddAsync(session);
			await _db.SaveChangesAsync();
		}

		// Путь: {userdata}/{userId}/{fileId}/temp/chunk_{chunkIndex}
		var chunkDir = Path.Combine("Data", "userData", userId, session.FileId.ToString(), "temp");
		Directory.CreateDirectory(chunkDir);
		var chunkPath = Path.Combine(chunkDir, $"chunk_{chunkIndex}");

		using (var stream = new FileStream(chunkPath, FileMode.Create))
		{
			await chunk.CopyToAsync(stream);
		}

		// Можно добавить трекинг загруженных чанков (если нужно)

		session.UpdatedAt = DateTime.UtcNow;
		await _db.SaveChangesAsync();

		return new OkObjectResult(new { message = "Чанк принят", chunkIndex });
	}


	public async Task<FileContentResultModel?> GetFileVersionAsync(string userId, Guid fileId, int version)
	{
		var session = await _db.FileUploadSessions
			.FirstOrDefaultAsync(s =>
				s.UserId == userId &&
				s.FileId == fileId &&
				s.Version == version &&
				s.Status == "complete");

		if (session == null)
			return null;

		var content = await _storage.ReadFileAsync(session.FileHash);

		return new FileContentResultModel
		{
			Content = content,
			FileName = session.OriginalFileName
		};
	}

	public async Task<bool> DeleteFileVersionAsync(string userId, Guid fileId, int version)
	{
		var session = await _db.FileUploadSessions
			.FirstOrDefaultAsync(s =>
				s.UserId == userId &&
				s.FileId == fileId &&
				s.Version == version &&
				s.Status == "complete");

		if (session == null)
			return false;

		_db.FileUploadSessions.Remove(session);
		await _db.SaveChangesAsync();

		await _storage.DeleteFileAsync(session.FileHash);
		return true;
	}
}
