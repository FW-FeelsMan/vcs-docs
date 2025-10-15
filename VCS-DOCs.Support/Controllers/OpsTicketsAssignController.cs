// Support/Controllers/OpsTicketsAssignController.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers;

[ApiController]
[Route("api/ops/tickets")]
[Authorize(Policy = "SupportOnly")]
public sealed class OpsTicketsAssignController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IHubContext<TicketHub> _hub;

    public OpsTicketsAssignController(ApplicationDbContext db, IHubContext<TicketHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public sealed class AssignDto
    {
        public string? UserId
        {
            get; set;
        }   // null → снять назначение
        public string? Mode
        {
            get; set;
        }     // опционально, по умолчанию "manual"
    }

    [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/assign")]
    [Authorize(Roles = "SupportAdmin")]
    public async Task<IActionResult> Assign([FromRoute] string id, [FromBody] AssignDto dto)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        if (string.Equals(t.Status, "closed", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { ok = false, error = "CLOSED", message = "Тикет закрыт." });

        var me = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var mode = string.IsNullOrWhiteSpace(dto.Mode) ? "manual" : dto.Mode.Trim().ToLowerInvariant();

        // Назначаем/снимаем
        t.AssignedUserId = string.IsNullOrWhiteSpace(dto.UserId) ? null : dto.UserId!.Trim();
        t.AssignedByUserId = me;
        t.AssignedAt = DateTime.UtcNow;
        t.AssignmentMode = mode;
        t.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        // Пуш в хаб — чтобы UI мог обновиться
        await _hub.Clients.All.SendAsync("assigned", new
        {
            ticketId = id,
            assignedUserId = t.AssignedUserId,
            assignedAt = t.AssignedAt,
            assignmentMode = t.AssignmentMode
        });

        return Ok(new
        {
            ok = true,
            ticketId = id,
            assignedUserId = t.AssignedUserId,
            assignedAt = t.AssignedAt,
            assignmentMode = t.AssignmentMode
        });
    }

    // Опционально: агент "забирает" неназначенный тикет
    [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/claim")]
    [Authorize(Policy = "SupportOnly")]
    public async Task<IActionResult> Claim([FromRoute] string id)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();
        if (string.Equals(t.Status, "closed", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { ok = false, error = "CLOSED" });

        if (t.AssignedUserId != null)  // уже назначен — нельзя
            return Conflict(new { ok = false, error = "ASSIGNED" });

        var me = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (me is null) return Forbid();

        t.AssignedUserId = me;
        t.AssignedByUserId = me;
        t.AssignedAt = DateTime.UtcNow;
        t.AssignmentMode = "manual";       // агент взял вручную
        t.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        await _hub.Clients.All.SendAsync("assigned", new
        {
            ticketId = id,
            assignedUserId = t.AssignedUserId,
            assignedAt = t.AssignedAt,
            assignmentMode = t.AssignmentMode
        });

        return Ok(new { ok = true, assignedUserId = t.AssignedUserId });
    }
}