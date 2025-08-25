// Models/Entities/SupportUserConnection.cs
using System.ComponentModel.DataAnnotations;

namespace VCS_DOCs.Models.Entities
{
    public class SupportUserConnection
    {
        [Key] public string ConnectionId { get; set; } = default!;
        [Required, MaxLength(64)] public string UserId { get; set; } = default!;
        public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
