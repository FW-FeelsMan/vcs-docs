namespace VCS_DOCs.Models.Entities
{
    public class SupportTicket
    {
        public string Id { get; set; } = default!;

        public bool EmailNotifyEnabled { get; set; } = true;

        public string? Subject
        {
            get; set;
        }

        /// <summary>open | closed</summary>
        public string Status { get; set; } = "open";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastIdleReminderAt
        {
            get; set;
        }

        public DateTime? UpdatedAt
        {
            get; set;
        }

        // --- Владелец тикета (пользователь) ---
        public string? OwnerUserId
        {
            get; set;
        }
        public string? OwnerLogin
        {
            get; set;
        }
        public string? ReplyToEmail
        {
            get; set;
        }

        // --- Назначение оператора ---
        /// <summary>Кому назначен тикет (оператор)</summary>
        public string? AssignedUserId
        {
            get; set;
        }

        /// <summary>Кто произвёл назначение (обычно админ; при auto-balance может быть null)</summary>
        public string? AssignedByUserId
        {
            get; set;
        }

        /// <summary>Когда был назначен</summary>
        public DateTime? AssignedAt
        {
            get; set;
        }

        /// <summary>"manual" | "auto" (ручное назначение админом или автобаланс)</summary>
        public string? AssignmentMode
        {
            get; set;
        }

        public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
    }
}