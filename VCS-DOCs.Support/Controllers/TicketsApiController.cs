// Support/Controllers/TicketsApiController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Data;

[ApiController]
[Route("api/support/tickets")]
[Authorize(Policy = "SupportDeskAccess")]
public class TicketsApiController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public TicketsApiController(ApplicationDbContext db) => _db = db;

    [HttpPost("{id}/email-notify")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEmailNotify(string id, [FromForm] bool enabled)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        t.EmailNotifyEnabled = enabled;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, enabled });
    }
}