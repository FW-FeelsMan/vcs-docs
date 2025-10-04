using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VCS_DOCs.Models.Entities;

[Table("SupportTicketAttachments")]
public sealed class SupportTicketAttachment
{
    [Key]
    public long Id
    {
        get; set;
    }

    [Required, MaxLength(32)]
    public string TicketId { get; set; } = default!;

    public long? MessageId
    {
        get; set;
    } // можно заполнить после создания сообщения

    [Required, MaxLength(260)]
    public string FileName { get; set; } = default!;

    [MaxLength(128)]
    public string? ContentType
    {
        get; set;
    }

    public long Size
    {
        get; set;
    }

    // ключ пути в сторадже: ticketId/uuid-filename
    [Required, MaxLength(512)]
    public string StorageKey { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? CreatedByUserId
    {
        get; set;
    }

    [MaxLength(32)]
    public string? CreatedByRole
    {
        get; set;
    } // "agent" | "user"
}