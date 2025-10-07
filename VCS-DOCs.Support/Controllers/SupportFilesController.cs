using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;

namespace VCS_DOCs.Support.Controllers;

public sealed class UploadsOptions
{
    public string Root { get; set; } = "";
    public int MaxSizeMb { get; set; } = 50;
    public string[] AllowedExtensions { get; set; } = Array.Empty<string>();
}

[ApiController]
[Route("api")]
public sealed class SupportFilesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IOptionsSnapshot<UploadsOptions> _opts;

    public SupportFilesController(ApplicationDbContext db, IWebHostEnvironment env, IOptionsSnapshot<UploadsOptions> opts)
    {
        _db = db;
        _env = env;
        _opts = opts;
    }

    // ===================== ОПЕРАТОР =====================

    [HttpPost("ops/tickets/{ticketId}/files")]
    [Authorize(Policy = "SupportOnly")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 200L * 1024 * 1024)]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public Task<IActionResult> UploadByOperator(string ticketId) =>
        UploadInternal(ticketId, createdByRole: "agent", enforceOwner: false);

    [HttpPost("ops/tickets/{ticketId}/files/bind")]
    [Authorize(Policy = "SupportOnly")]
    public Task<IActionResult> BindToMessageByOperator(string ticketId, [FromBody] BindDto dto) =>
        BindInternal(ticketId, dto, enforceOwner: false);

    // ===================== ПОЛЬЗОВАТЕЛЬ =====================

    [HttpPost("user/tickets/{ticketId}/files")]
    [Authorize(Policy = "SupportDeskAccess")]
    [Consumes("multipart/form-data")]
    [RequestFormLimits(MultipartBodyLengthLimit = 200L * 1024 * 1024)]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public Task<IActionResult> UploadByUser(string ticketId) =>
        UploadInternal(ticketId, createdByRole: "user", enforceOwner: true);

    [HttpPost("user/tickets/{ticketId}/files/bind")]
    [Authorize(Policy = "SupportDeskAccess")]
    public Task<IActionResult> BindToMessageByUser(string ticketId, [FromBody] BindDto dto) =>
        BindInternal(ticketId, dto, enforceOwner: true);

    // ===================== СКАЧИВАНИЕ =====================

    [HttpGet("support/files/{id:long}")]
    [Authorize(Policy = "SupportDeskAccess")]
    public async Task<IActionResult> Download(long id)
    {
        var att = await _db.SupportTicketAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (att == null) return NotFound();

        // проверка доступа к тикету
        var t = await _db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == att.TicketId);
        if (t == null) return NotFound();

        var isSupport = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
        if (!isSupport)
        {
            var uid = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var login = User.Identity?.Name;
            var owner = (!string.IsNullOrEmpty(t.OwnerUserId) && t.OwnerUserId == uid)
                        || (!string.IsNullOrEmpty(t.OwnerLogin) && t.OwnerLogin == login);
            if (!owner) return Forbid();
        }

        var set = _opts.Get("Support");
        var root = string.IsNullOrWhiteSpace(set.Root)
            ? Path.Combine(_env.ContentRootPath, "App_Data", "SupportFiles")
            : set.Root;

        var full = Path.Combine(root, att.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(full)) return NotFound();

        var ct = string.IsNullOrWhiteSpace(att.ContentType) ? GetContentType(att.FileName) : att.ContentType!;
        var fs = System.IO.File.OpenRead(full);
        return File(fs, ct, fileDownloadName: att.FileName);
    }

    // ===================== INTERNALS =====================

    public sealed record BindDto(long[] AttachmentIds, long MessageId);

    private async Task<IActionResult> UploadInternal(string ticketId, string createdByRole, bool enforceOwner)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
            return BadRequest(new { ok = false, error = "no_ticket" });

        var t = await _db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticketId);
        if (t == null) return NotFound(new { ok = false, error = "ticket_not_found" });

        if (enforceOwner)
        {
            var uid = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var login = User.Identity?.Name;
            var isOwner = (!string.IsNullOrEmpty(t.OwnerUserId) && t.OwnerUserId == uid)
                          || (!string.IsNullOrEmpty(t.OwnerLogin) && t.OwnerLogin == login);
            if (!isOwner) return Forbid();
        }

        if (!Request.HasFormContentType)
            return StatusCode(415, new { ok = false, error = "not_multipart" });

        var files = Request.Form.Files;
        if (files == null || files.Count == 0)
            return BadRequest(new { ok = false, error = "no_files" });

        var set = _opts.Get("Support");
        var root = string.IsNullOrWhiteSpace(set.Root)
            ? Path.Combine(_env.ContentRootPath, "App_Data", "SupportFiles")
            : set.Root;

        Directory.CreateDirectory(root);

        long max = Math.Max(1, set.MaxSizeMb) * 1024L * 1024L;

        var allowed = (set.AllowedExtensions ?? Array.Empty<string>())
            .Select(e => (e ?? "").Trim())
            .Where(e => e.Length > 0)
            .Select(e => e.StartsWith(".") ? e.ToLowerInvariant() : ("." + e.ToLowerInvariant()))
            .ToHashSet();

        var userId = User?.Identity?.IsAuthenticated == true
            ? User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null;

        var saved = new List<object>();
        foreach (var f in files)
        {
            if (f.Length <= 0) continue;
            if (f.Length > max)
                return BadRequest(new { ok = false, error = "file_too_big", maxMb = set.MaxSizeMb });

            var safeName = SanitizeFileName(f.FileName);
            var ext = Path.GetExtension(safeName).ToLowerInvariant();
            if (allowed.Count > 0 && !allowed.Contains(ext))
                return BadRequest(new { ok = false, error = "bad_ext", ext, allowed });

            var key = $"{ticketId}/{Guid.NewGuid():N}-{safeName}";
            var full = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);

            await using (var fs = System.IO.File.Create(full))
            {
                await f.CopyToAsync(fs);
            }

            var att = new SupportTicketAttachment
            {
                TicketId = ticketId,
                FileName = safeName,
                ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? GetContentType(safeName) : f.ContentType,
                Size = f.Length,
                StorageKey = key,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = userId,
                CreatedByRole = createdByRole
            };
            _db.SupportTicketAttachments.Add(att);
            await _db.SaveChangesAsync();

            saved.Add(new
            {
                id = att.Id,
                name = att.FileName,
                size = att.Size,
                contentType = att.ContentType,
                url = Url.ActionLink(nameof(Download), values: new
                {
                    id = att.Id
                })
            });
        }

        return Ok(new { ok = true, files = saved });
    }

    private async Task<IActionResult> BindInternal(string ticketId, BindDto dto, bool enforceOwner)
    {
        if (dto.AttachmentIds == null || dto.AttachmentIds.Length == 0)
            return Ok(new { ok = true, changed = 0 });

        var t = await _db.SupportTickets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ticketId);
        if (t == null) return NotFound(new { ok = false, error = "ticket_not_found" });

        string? uid = null;
        if (enforceOwner)
        {
            uid = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var login = User.Identity?.Name;
            var isOwner = (!string.IsNullOrEmpty(t.OwnerUserId) && t.OwnerUserId == uid)
                          || (!string.IsNullOrEmpty(t.OwnerLogin) && t.OwnerLogin == login);
            if (!isOwner) return Forbid();
        }

        var q = _db.SupportTicketAttachments.Where(a => a.TicketId == ticketId && dto.AttachmentIds.Contains(a.Id));
        if (enforceOwner)
            q = q.Where(a => a.CreatedByUserId == uid);

        var changed = await q.ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, dto.MessageId));
        return Ok(new { ok = true, changed });
    }

    // ===================== helpers =====================

    private static string SanitizeFileName(string name)
    {
        var n = Path.GetFileName(name);
        n = Regex.Replace(n, @"[^\w\.\- \(\)\[\]#@А-Яа-яЁё]", "_");
        if (n.Length > 200) n = n[^200..];
        return n;
    }

    private static string GetContentType(string fileName)
    {
        var prov = new FileExtensionContentTypeProvider();
        return prov.TryGetContentType(fileName, out var ct) ? ct : "application/octet-stream";
    }
}
