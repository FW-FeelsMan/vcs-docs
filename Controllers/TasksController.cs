using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using VCS_DOCs;
using VCS_DOCs.Services;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TasksController : ControllerBase
{
	private readonly ApplicationDbContext _db;
	private readonly UploadCleanupService _cleanupService;

	public TasksController(ApplicationDbContext db, UploadCleanupService cleanupService)
	{
		_db = db;
		_cleanupService = cleanupService;
	}

	[HttpGet("active")]
	public IActionResult GetActiveTasks()
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(userId))
			return Unauthorized();

		var now = DateTime.Now;
		var nextIncomplete = UploadCleanupService.LastIncompleteRun.AddMinutes(15);
		var nextCompiling = UploadCleanupService.LastCompilingRun.AddMinutes(15);

		var tasks = new List<object>
	{
		new {
			title = "Очистка INCOMPLETE",
			statusText = $"Автозапуск: {FormatTime((int)(nextIncomplete - now).TotalSeconds)}",
			statusClass = "waiting",
			type = "system",
			cancelable = false,
			manualTrigger = true,
			taskKey = "uploadCleanup_incomplete",
			nextRunUtc = nextIncomplete.ToString("o")
		},
		new {
			title = "Очистка COMPILING",
			statusText = $"Автозапуск: {FormatTime((int)(nextCompiling - now).TotalSeconds)}",
			statusClass = "waiting",
			type = "system",
			cancelable = false,
			manualTrigger = true,
			taskKey = "uploadCleanup_compiling",
			nextRunUtc = nextCompiling.ToString("o")
		},
		new {
			title = "Контроль входа с одного устройства",
			statusText = "Активна",
			statusClass = "active",
			type = "system",
			cancelable = false
		}
	};

		return Ok(tasks);
	}


	[HttpPost("trigger")]
	public async Task<IActionResult> TriggerTask([FromBody] TriggerRequest request)
	{
		var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (string.IsNullOrEmpty(userId))
			return Unauthorized();

		switch (request.TaskKey)
		{
			case "uploadCleanup_incomplete":
				await _cleanupService.RunIncompleteCleanupAsync(force: true);
				return Ok(new { message = "Очистка incomplete запущена вручную." });

			case "uploadCleanup_compiling":
				await _cleanupService.RunCompilingCleanupAsync(force: true);
				return Ok(new { message = "Очистка compiling запущена вручную." });

			default:
				return BadRequest(new { error = "Неизвестный ключ задачи." });
		}
	}

	private string FormatTime(int seconds)
	{
		if (seconds < 60) return $"{seconds} сек.";
		if (seconds < 3600) return $"{seconds / 60} мин.";
		return $"{seconds / 3600} ч.";
	}
}

public class TriggerRequest
{
	public string TaskKey { get; set; } = string.Empty;
}
