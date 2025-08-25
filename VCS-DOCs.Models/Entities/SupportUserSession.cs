using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace VCS_DOCs.Models.Entities
{
    [Table("SupportUserSessions")]
    public class SupportUserSession
    {
        [Key]
        [MaxLength(64)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? JwtId
        {
            get; set;
        }

        public bool IsOnline
        {
            get; set;
        }

        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}
