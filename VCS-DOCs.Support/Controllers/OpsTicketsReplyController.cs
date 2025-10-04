using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsTicketsReplyController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public OpsTicketsReplyController(ApplicationDbContext db) => _db = db;

    public sealed record ReplyDto(string? Text, long[]? Attachments);

    [HttpPost("{ticketId}/reply")]
    public async Task<IActionResult> Reply(string ticketId, [FromBody] ReplyDto dto)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == ticketId && x.Status != "closed");
        if (t == null) return NotFound(new { ok = false, error = "ticket_not_found" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized(new { ok = false });

        var now = DateTime.UtcNow;

        var msg = new SupportTicketMessage
        {
            TicketId = t.Id,
            AuthorUserId = userId,
            AuthorRole = "agent",
            Body = string.IsNullOrWhiteSpace(dto.Text) ? "(файлы)" : dto.Text!.Trim(),
            CreatedAt = now
        };

        _db.SupportTicketMessages.Add(msg);
        t.UpdatedAt = now;

        await _db.SaveChangesAsync(); // чтобы появился msg.Id

        if (dto.Attachments is { Length: > 0 })
        {
            await _db.SupportTicketAttachments
                .Where(a => a.TicketId == t.Id && dto.Attachments.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, msg.Id));
        }

        // TODO: при желании дёрнуть SignalR-хаб, чтобы прилетало «в реальном времени»

        return Ok(new { ok = true, messageId = msg.Id });
    }
}