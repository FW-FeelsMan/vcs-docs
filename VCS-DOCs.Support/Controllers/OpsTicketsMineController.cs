using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsTicketsMineController : ControllerBase
{
	private readonly ApplicationDbContext _db;

	public OpsTicketsMineController(ApplicationDbContext db)
	{
		_db = db;
	}

	private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

	[HttpGet("my-closed")]
	public async Task<IActionResult> MyClosed([FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] string? q = null)
	{
		var me = CurrentUserId();
		if (string.IsNullOrWhiteSpace(me)) return Forbid();

		static DateTime ToUtc(DateTime dt)
		{
			// JS присылает ISO-строки (обычно с 'Z' => UTC). Если Kind не задан — считаем, что это уже UTC.
			return dt.Kind switch
			{
				DateTimeKind.Utc => dt,
				DateTimeKind.Local => dt.ToUniversalTime(),
				_ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
			};
		}

		// “Мной закрытые” = заявки, закрытые именно этим оператором.
		// Закрытие фиксируется системным сообщением: "Заявка закрыта оператором." (AuthorRole=agent, AuthorUserId=current).
		// ВАЖНО: не используем EF.Functions.DateDiff* — SQLite их не поддерживает.
		var query =
			from m in _db.SupportTicketMessages.AsNoTracking()
			join t in _db.SupportTickets.AsNoTracking() on m.TicketId equals t.Id
			where t.Status == "closed"
				  && m.AuthorRole != "user"
				  && m.AuthorUserId == me
				  && m.Body == "Заявка закрыта оператором."
			join u in _db.Users.AsNoTracking() on t.OwnerUserId equals u.Id into uJoin
			from u in uJoin.DefaultIfEmpty()
			select new
			{
				t.Id,
				t.Subject,
				t.CreatedAt,
				ClosedAt = m.CreatedAt,
				OwnerLogin = t.OwnerLogin ?? u.UserName,
				Organization = u.Organization,
				OperatorReplies = _db.SupportTicketMessages.AsNoTracking()
					.Count(x => x.TicketId == t.Id && x.AuthorRole != "user"),
				UserReplies = _db.SupportTicketMessages.AsNoTracking()
					.Count(x => x.TicketId == t.Id && x.AuthorRole == "user")
			};

		if (from.HasValue)
		{
			var f = ToUtc(from.Value);
			query = query.Where(x => x.ClosedAt >= f);
		}

		if (to.HasValue)
		{
			var t = ToUtc(to.Value);
			query = query.Where(x => x.ClosedAt <= t);
		}

		var rows = await query
			.OrderByDescending(x => x.ClosedAt)
			.ToListAsync();

		if (!string.IsNullOrWhiteSpace(q))
		{
			var s = q.Trim().ToLowerInvariant();
			rows = rows
				.Where(x =>
					((x.Id ?? string.Empty) + " " + (x.Subject ?? string.Empty) + " " + (x.OwnerLogin ?? string.Empty) + " " + (x.Organization ?? string.Empty))
					.ToLowerInvariant()
					.Contains(s))
				.ToList();
		}

		var result = rows.Select(x =>
		{
			var closedAt = x.ClosedAt;
			var createdAt = x.CreatedAt;
			var mins = (int)Math.Round((closedAt - createdAt).TotalMinutes);
			if (mins < 0) mins = 0;

			return new
			{
				id = x.Id,
				subject = x.Subject ?? "(без темы)",
				organization = x.Organization ?? string.Empty,
				closedAt,
				resolutionMinutes = mins,
				replies = new
				{
					user = x.UserReplies,
					op = x.OperatorReplies
				},
				createdAt
			};
		});

		return Ok(result);
	}
}