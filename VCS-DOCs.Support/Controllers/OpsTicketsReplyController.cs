using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
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
    private readonly IWebHostEnvironment _env;

    // 200 МБ — синхронизировано с Program.cs (Kestrel + FormOptions)
    private const long MaxUploadBytes = 200L * 1024 * 1024;

    public OpsTicketsReplyController(ApplicationDbContext db, IHubContext<TicketHub> hub, IWebHostEnvironment env)
    {
        _db = db;
        _hub = hub;
        _env = env;
    }

    public sealed class ReplyDto
    {
        public string? Text
        {
            get; set;
        }
        // поддерживаем оба поля
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
    /// Загрузка файлов оператором. Складывает в App_Data/SupportFiles/{ticketId}/{attachmentId}
    /// </summary>
    [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/files")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(
        MultipartBodyLengthLimit = MaxUploadBytes,
        ValueLengthLimit = int.MaxValue,
        MultipartHeadersLengthLimit = int.MaxValue)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadFiles([FromRoute] string id)
    {
        try
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound(new { ok = false, error = "NOT_FOUND", message = "Заявка не найдена." });

            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAA = User.IsInRole("SupportAgent") || User.IsInRole("SupportAdmin");
            if (!isAA) return Forbid();

            var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
            var all = form.Files;
            if (all is null || all.Count == 0)
                return BadRequest(new { ok = false, error = "NO_FILES", message = "Файлы не переданы." });

            var valid = all.Where(f => f is not null && f.Length > 0).ToList();
            if (valid.Count == 0)
                return BadRequest(new { ok = false, error = "EMPTY_FILES", message = "Переданные файлы пустые." });

            var root = Path.Combine(_env.ContentRootPath, "App_Data", "SupportFiles", id);
            Directory.CreateDirectory(root);

            var saved = new List<object>();

            foreach (var f in valid)
            {
                var originalName = Path.GetFileName(f.FileName ?? "file.bin");
                var size = f.Length;

                var att = new SupportTicketAttachment
                {
                    TicketId = id,
                    MessageId = null,
                    FileName = originalName,
                    ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType,
                    Size = size,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = uid,
                    CreatedByRole = "agent",
                    StorageKey = $"{id}/{Guid.NewGuid():N}"
                };

                _db.SupportTicketAttachments.Add(att);
                await _db.SaveChangesAsync(); // нужен att.Id

                var destPath = Path.Combine(root, att.Id.ToString());
                try
                {
                    await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await f.CopyToAsync(fs, HttpContext.RequestAborted);
                }
                catch
                {
                    try
                    {
                        _db.SupportTicketAttachments.Remove(att);
                        await _db.SaveChangesAsync();
                    }
                    catch { }
                    throw;
                }

                // (необязательно) синхронизировать StorageKey с "id/Id":
                // att.StorageKey = $"{id}/{att.Id}";
                // await _db.SaveChangesAsync();

                saved.Add(new { id = att.Id, name = att.FileName, size = att.Size });
            }

            return Ok(new { ok = true, files = saved });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new { ok = false, error = "CLIENT_ABORTED", message = "Клиент отменил загрузку." });
        }
        catch (InvalidDataException ex)
        {
            return BadRequest(new { ok = false, error = "BAD_MULTIPART", message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { ok = false, error = "UPLOAD_FAIL", message = ex.Message });
        }
    }

    /// <summary>
    /// Ответ оператора (с привязкой ранее загруженных файлов).
    /// </summary>
    [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/reply")]
    public async Task<IActionResult> Reply([FromRoute] string id, [FromBody] ReplyDto dto)
    {
        var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
        if (t is null) return NotFound();

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAA = User.IsInRole("SupportAgent") || User.IsInRole("SupportAdmin");
        if (!isAA) return Forbid();

        var text = (dto.Text ?? string.Empty).Trim();
        if (text.Length > 1500)
            return BadRequest(new { ok = false, error = "MAX_LEN", message = "Сообщение не должно превышать 1500 символов." });

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

        var login = await _db.Users.Where(u => u.Id == uid).Select(u => u.UserName).FirstOrDefaultAsync();
        var authorName = $"Оператор#{(login ?? "operator")}";
        var authorAvatarUrl = uid != null ? $"/avatars/{uid}.jpg" : "/avatars/none.jpg";

        List<object> attachments;
        if (attIds.Length == 0)
        {
            attachments = new List<object>();
        }
        else
        {
            attachments = await _db.SupportTicketAttachments
                .Where(a => a.TicketId == id && a.MessageId == msg.Id)
                .OrderBy(a => a.Id)
                .Select(a => (object)new { id = a.Id, name = a.FileName, size = a.Size })
                .ToListAsync();
        }

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