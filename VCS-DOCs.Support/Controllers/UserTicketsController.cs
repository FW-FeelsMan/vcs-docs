using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Auth;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Models.Entities;
using VCS_DOCs.Support.Hubs;

namespace VCS_DOCs.Support.Controllers
{
    [EnableRateLimiting("api-burst")]
    /// <summary>
    /// Пользовательский API для заявок (список моих заявок, сообщения, загрузка/привязка файлов).
    /// </summary>
    [ApiController]
    [Route("api/support/tickets")]
    [Authorize(Policy = "SupportDeskAccess")]
    public sealed class UserTicketsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IHubContext<TicketHub> _hub;
        private readonly ILogger<UserTicketsController> _log;
        private readonly IWebHostEnvironment _env;

        private const string TicketIdRoute = "{id:regex(^[[0-9a-fA-F]]{{8}}$)}";
        private const long MaxUploadBytes = 200L * 1024 * 1024; // 200 MB — в ногу с Program.cs

        public UserTicketsController(
            ApplicationDbContext db,
            IHubContext<TicketHub> hub,
            ILogger<UserTicketsController> log,
            IWebHostEnvironment env)
        {
            _db = db;
            _hub = hub;
            _log = log;
            _env = env;
        }

        private (string? userId, string? userName, bool isAgentOrAdmin) GetMe()
        {
            var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var uname = User.Identity?.Name;
            var isAA = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
            return (uid, uname, isAA);
        }

        private IQueryable<SupportTicket> QueryMyTickets()
        {
            var (uid, uname, isAA) = GetMe();
            if (isAA) return _db.SupportTickets.AsNoTracking();

            return _db.SupportTickets.AsNoTracking()
                .Where(t => t.OwnerUserId == uid || (t.OwnerUserId == null && t.OwnerLogin == uname));
        }

        [HttpGet("my")]
        public async Task<IActionResult> My([FromQuery] string? status = "open")
        {
            var q = QueryMyTickets();
            if (!string.IsNullOrWhiteSpace(status))
                q = q.Where(t => t.Status == status);

            var list = await q
                .OrderByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    createdAt = t.CreatedAt,
                    updatedAt = t.UpdatedAt,
                    lastMessageAt = t.Messages
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => (DateTime?)m.CreatedAt)
                        .FirstOrDefault(),
                    lastMessage = t.Messages
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(m => m.Body)
                        .FirstOrDefault()
                })
                .ToListAsync();

            static string Snip(string? s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length > 160 ? s[..160] + "…" : s;
            }

            var dto = list.Select(x => new
            {
                id = x.Id,
                subject = x.Subject,
                status = x.Status,
                createdAt = x.createdAt,
                updatedAt = x.updatedAt,
                lastMessageAt = x.lastMessageAt ?? x.updatedAt ?? x.createdAt,
                lastSnippet = Snip(x.lastMessage)
            });

            return Ok(new { items = dto });
        }

        [HttpGet(TicketIdRoute)]
        public async Task<IActionResult> GetOne([FromRoute] string id)
        {
            var one = await _db.SupportTickets.AsNoTracking()
                .Where(t => t.Id == id)
                .Select(t => new
                {
                    t.Id,
                    t.Subject,
                    t.Status,
                    t.CreatedAt,
                    t.UpdatedAt,
                    t.OwnerUserId,
                    t.OwnerLogin
                })
                .FirstOrDefaultAsync();

            if (one is null) return NotFound();

            var (uid, uname, isAA) = GetMe();
            var isMine = isAA || one.OwnerUserId == uid || (one.OwnerUserId == null && one.OwnerLogin == uname);
            if (!isMine) return Forbid();

            var messagesRaw = await _db.SupportTicketMessages.AsNoTracking()
                .Where(m => m.TicketId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.Id,
                    role = m.AuthorRole,
                    body = m.Body,
                    createdAt = m.CreatedAt,
                    authorUserId = m.AuthorUserId
                })
                .ToListAsync();

            var msgIds = messagesRaw.Select(m => m.id).ToArray();

            var authorIds = messagesRaw.Select(m => m.authorUserId)
                                       .Where(s => s != null)
                                       .Distinct()
                                       .Cast<string>()
                                       .ToArray();

            var userNameById = (authorIds.Length == 0)
                ? new Dictionary<string, string>()
                : await _db.Users.AsNoTracking()
                    .Where(u => authorIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName })
                    .ToDictionaryAsync(x => x.Id, x => x.UserName ?? x.Id);

            string DisplayName(string? role, string? authorUserId)
            {
                userNameById.TryGetValue(authorUserId ?? "", out var login);
                if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                    return login ?? one.OwnerLogin ?? "Пользователь";
                var tag = login ?? "operator";
                return $"Оператор#{tag}";
            }

            var attByMsg = (msgIds.Length == 0)
                ? new Dictionary<long, List<object>>()
                : await _db.SupportTicketAttachments.AsNoTracking()
                    .Where(s => s.TicketId == id && s.MessageId != null && msgIds.Contains(s.MessageId.Value))
                    .OrderBy(s => s.MessageId)
                    .GroupBy(s => s.MessageId!.Value)
                    .ToDictionaryAsync(
                        g => g.Key,
                        g => g.Select(s => (object)new { id = s.Id, name = s.FileName, size = s.Size }).ToList()
                    );

            var messages = messagesRaw.Select(m => new
            {
                m.id,
                m.role,
                m.body,
                m.createdAt,
                mine = m.authorUserId == GetMe().userId,
                authorName = DisplayName(m.role, m.authorUserId),
                authorAvatarUrl = m.authorUserId != null ? $"/avatars/{m.authorUserId}.jpg" : "/avatars/none.jpg",
                attachments = attByMsg.TryGetValue(m.id, out var list) ? (IEnumerable<object>)list : Array.Empty<object>()
            });

            return Ok(new { ticket = one, messages });
        }

        public sealed class NewMessageDto
        {
            public string? Body
            {
                get; set;
            }
            public string[]? AttachmentIds
            {
                get; set;
            } // опционально: если клиент решит прислать одним вызовом
        }

        [HttpPost(TicketIdRoute + "/messages")]
        public async Task<IActionResult> PostMessage([FromRoute] string id, [FromBody] NewMessageDto dto)
        {
            var body = dto.Body ?? string.Empty;
            if (body.Length > 1500)
                return BadRequest(new { ok = false, error = "MAX_LEN", message = "Сообщение не должно превышать 1500 символов." });

            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound();

            var (uid, uname, isAA) = GetMe();
            var isMine = isAA || t.OwnerUserId == uid || (t.OwnerUserId == null && t.OwnerLogin == uname);
            if (!isMine) return Forbid();

            var role = isAA ? "agent" : "user";

            var msg = new SupportTicketMessage
            {
                TicketId = id,
                AuthorUserId = uid,
                AuthorRole = role,
                Body = body,
                CreatedAt = DateTime.UtcNow
            };
            _db.SupportTicketMessages.Add(msg);

            t.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // имя/аватар автора для пуша
            string authorName;
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                var login = await _db.Users.Where(u => u.Id == uid).Select(u => u.UserName).FirstOrDefaultAsync();
                authorName = login ?? t.OwnerLogin ?? "Пользователь";
            }
            else
            {
                var login = await _db.Users.Where(u => u.Id == uid).Select(u => u.UserName).FirstOrDefaultAsync();
                authorName = $"Оператор#{(login ?? "operator")}";
            }
            var authorAvatarUrl = uid != null ? $"/avatars/{uid}.jpg" : "/avatars/none.jpg";

            // если клиент прислал список attachmentIds прямо сюда — привяжем
            var payload = new { attachmentIds = dto.AttachmentIds ?? Array.Empty<string>() };
            var attIds = payload.attachmentIds ?? Array.Empty<string>();
            var attLongs = attIds
                .Select(s => long.TryParse(s, out var v) ? (long?)v : null)
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToArray();

            List<SupportTicketAttachment> bound = new();
            if (attLongs.Length > 0)
            {
                bound = await _db.SupportTicketAttachments
                    .Where(a => a.TicketId == id && a.MessageId == null && attLongs.Contains(a.Id))
                    .ToListAsync();

                foreach (var a in bound) a.MessageId = msg.Id;
                await _db.SaveChangesAsync();
            }

            // realtime push (включая имя/аватар/вложения)
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
                    attachments = bound.Select(a => new { id = a.Id, name = a.FileName, size = a.Size }).ToArray()
                }
            });

            return Ok(new { ok = true, id = msg.Id, at = msg.CreatedAt, authorName, authorUserId = uid, authorAvatarUrl });
        }

        [HttpPost("{id:regex(^[[0-9a-fA-F]]{{8}}$)}/close")]
        public async Task<IActionResult> Close([FromRoute] string id)
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id);
            if (t is null) return NotFound();

            var isAA = User.IsInRole(Roles.SupportAgent) || User.IsInRole(Roles.SupportAdmin);
            if (!isAA) return Forbid();

            if (t.Status == "closed")
                return Ok(new { ok = true, status = "closed", updatedAt = t.UpdatedAt });

            t.Status = "closed";
            t.UpdatedAt = DateTime.UtcNow;

            _db.SupportTicketMessages.Add(new SupportTicketMessage
            {
                TicketId = id,
                AuthorUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                AuthorRole = "agent",
                Body = "Заявка закрыта оператором.",
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"ticket:{id}").SendAsync("status", new
            {
                ticketId = id,
                status = "closed",
                updatedAt = t.UpdatedAt
            });

            return Ok(new { ok = true, status = "closed", updatedAt = t.UpdatedAt });
        }

        // ========================= ФАЙЛЫ (upload + bind) =========================

        private static bool IsMineOrAA(bool isAA, SupportTicket t, string? uid, string? uname)
            => isAA || t.OwnerUserId == uid || (t.OwnerUserId == null && t.OwnerLogin == uname);

        public sealed class BindDto
        {
            public long[]? AttachmentIds
            {
                get; set;
            }
            public long? MessageId
            {
                get; set;
            }
        }

        // Upload (два маршрута: support и user)
        [HttpPost(TicketIdRoute + "/files")]
        [HttpPost("/api/user/tickets/{id:regex(^[[0-9a-fA-F]]{{8}}$)}/files")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes, ValueLengthLimit = int.MaxValue, MultipartHeadersLengthLimit = int.MaxValue)]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadFiles([FromRoute] string id, CancellationToken ct)
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return NotFound(new { ok = false, error = "NOT_FOUND", message = "Заявка не найдена." });

            var (uid, uname, isAA) = GetMe();
            if (!IsMineOrAA(isAA, t, uid, uname)) return Forbid();

            var form = await Request.ReadFormAsync(ct);
            var files = form.Files;
            if (files is null || files.Count == 0)
                return BadRequest(new { ok = false, error = "NO_FILES", message = "Файлы не переданы." });

            var valid = files.Where(f => f is not null && f.Length > 0).ToList();
            if (valid.Count == 0)
                return BadRequest(new { ok = false, error = "EMPTY_FILES", message = "Переданные файлы пустые." });

            var root = Path.Combine(_env.ContentRootPath, "App_Data", "SupportFiles", id);
            Directory.CreateDirectory(root);

            var saved = new List<object>();
            var createdByRole = isAA ? "agent" : "user";

            foreach (var f in valid)
            {
                var originalName = Path.GetFileName(f.FileName ?? "file.bin");
                var size = f.Length;

                // ВАЖНО: Заполняем все обязательные поля, в т.ч. StorageKey
                var att = new SupportTicketAttachment
                {
                    TicketId = id,
                    MessageId = null,
                    FileName = originalName,
                    ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType,
                    Size = size,
                    CreatedAt = DateTime.UtcNow,
                    CreatedByUserId = uid,
                    CreatedByRole = createdByRole,
                    // StorageKey может быть любым непустым ключом хранилища; используем GUID.
                    // Если хотите, чтобы совпадал с путём на диске — см. комментарий ниже про "finalKey".
                    StorageKey = $"{id}/{Guid.NewGuid():N}"
                };

                _db.SupportTicketAttachments.Add(att);
                await _db.SaveChangesAsync(ct); // нужен att.Id

                var destPath = Path.Combine(root, att.Id.ToString());
                try
                {
                    await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                    await f.CopyToAsync(fs, ct);
                }
                catch
                {
                    try
                    {
                        _db.SupportTicketAttachments.Remove(att);
                        await _db.SaveChangesAsync(ct);
                    }
                    catch { }
                    throw;
                }

                // (необязательно) Если хотите, чтобы StorageKey отражал финальный «путь» в вашем сторадже,
                // можно обновить после получения Id:
                // var finalKey = $"{id}/{att.Id}";
                // att.StorageKey = finalKey;
                // await _db.SaveChangesAsync(ct);

                saved.Add(new { id = att.Id, name = att.FileName, size = att.Size });
            }

            return Ok(new { ok = true, files = saved });
        }

        // Bind (два маршрута: support и user)
        [HttpPost(TicketIdRoute + "/files/bind")]
        [HttpPost("/api/user/tickets/{id:regex(^[[0-9a-fA-F]]{{8}}$)}/files/bind")]
        public async Task<IActionResult> BindFiles([FromRoute] string id, [FromBody] BindDto dto, CancellationToken ct)
        {
            var t = await _db.SupportTickets.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return NotFound(new { ok = false, error = "NOT_FOUND" });

            var (uid, uname, isAA) = GetMe();
            if (!IsMineOrAA(isAA, t, uid, uname)) return Forbid();

            var attIds = (dto.AttachmentIds ?? Array.Empty<long>()).Distinct().ToArray();
            if (attIds.Length == 0 || dto.MessageId is null)
                return BadRequest(new { ok = false, error = "BAD_REQUEST", message = "attachmentIds/messageId отсутствуют." });

            await _db.SupportTicketAttachments
                .Where(a => a.TicketId == id && a.MessageId == null && attIds.Contains(a.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, dto.MessageId!.Value), ct);

            return Ok(new { ok = true });
        }
    }
}
