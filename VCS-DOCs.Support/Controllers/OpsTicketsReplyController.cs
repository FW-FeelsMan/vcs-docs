// Support/Controllers/OpsTicketsReplyController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsTicketsReplyController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<TicketHub> _hub;

    public OpsTicketsReplyController(ApplicationDbContext db, IHubContext<TicketHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public sealed class ReplyDto
    {
        public string? Text
        {
            get; set;
        }
        // поддерживаем оба поля для совместимости фронтов
        public long[]? AttachmentIds
        {
            get; set;
        }
        public long[]? Attachments
        {
            get; set;
        }
    }

    /// <summary>
    /// Ответ оператора (с привязкой ранее загруженных файлов).
    /// Разрешено:
    ///   - Админу всегда
    ///   - Агенту, если тикет неназначен ИЛИ назначен на него
    /// </summary>
    [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/reply")]
    public async Task<IActionResult> Reply([FromRoute] string id, [FromBody] ReplyDto dto)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("SupportAdmin");
        var isAgent = User.IsInRole("SupportAgent") || isAdmin;
        if (!isAgent) return Forbid();

        // если тикет уже назначен на другого оператора — запрещаем не-админу
        if (!isAdmin && !string.IsNullOrWhiteSpace(t.AssignedUserId) && t.AssignedUserId != uid)
            return Forbid();

        var text = (dto.Text ?? string.Empty).Trim();
        if (text.Length > 1500)
            return BadRequest(new { ok = false, error = "MAX_LEN", message = "Сообщение не должно превышать 1500 символов." });

        // авто-назначение: если тикет не назначен — назначаем на текущего агента
        var assignedJustNow = false;
        if (string.IsNullOrWhiteSpace(t.AssignedUserId))
        {
            t.AssignedUserId = uid;
            t.AssignedByUserId = uid;
            t.AssignedAt = DateTime.UtcNow;
            t.AssignmentMode = t.AssignmentMode ?? "manual";
            assignedJustNow = true;
        }

        var msg = new SupportTicketMessage
        {
            TicketId = id,
            AuthorUserId = uid,
            AuthorRole = "agent",
            Body = text,
            CreatedAt = DateTime.UtcNow
        };

        _db.SupportTicketMessages.Add(msg);
        t.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(); // нужен msg.Id

        var attIds = (dto.AttachmentIds ?? Array.Empty<long>())
            .Concat(dto.Attachments ?? Array.Empty<long>())
            .Distinct()
            .ToArray();

        if (attIds.Length > 0)
        {
            await _db.SupportTicketAttachments
                .Where(a => a.TicketId == id && a.MessageId == null && attIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, msg.Id));
        }

        var login = await _db.Users
            .Where(u => u.Id == uid)
            .Select(u => u.UserName)
            .FirstOrDefaultAsync();

        var authorName = $"Оператор#{(login ?? "operator")}";
        var authorAvatarUrl = uid != null ? $"/avatars/{uid}.jpg" : "/avatars/none.jpg";

        var attachments = (attIds.Length == 0)
            ? new List<object>()
            : await _db.SupportTicketAttachments
                .Where(a => a.TicketId == id && a.MessageId == msg.Id)
                .OrderBy(a => a.Id)
                .Select(a => (object)new { id = a.Id, name = a.FileName, size = a.Size })
                .ToListAsync();

        // realtime: новое сообщение
        await _hub.Clients.Group($"ticket:{id}").SendAsync("message", new
        {
            ticketId = id,
            message = new
            {
                id = msg.Id,
                role = msg.AuthorRole,
                body = msg.Body,
                createdAt = msg.CreatedAt,
                authorUserId = msg.AuthorUserId,
                authorName,
                authorAvatarUrl,
                attachments
            }
        });

        // realtime: если только что назначили — отправим событие "assigned"
        if (assignedJustNow)
        {
            await _hub.Clients.All.SendAsync("assigned", new
            {
                ticketId = id,
                assignedUserId = t.AssignedUserId,
                assignedAt = t.AssignedAt,
                assignmentMode = t.AssignmentMode
            });
        }

        return Ok(new
        {
            ok = true,
            messageId = msg.Id,
            at = msg.CreatedAt,
            authorUserId = msg.AuthorUserId,
            authorName,
            authorAvatarUrl
        });
    }
}