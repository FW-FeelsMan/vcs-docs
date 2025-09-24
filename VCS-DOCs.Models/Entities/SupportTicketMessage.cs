namespace VCS_DOCs.Models.Entities
{
    public class SupportTicketMessage
    {
        public long Id
        {
            get; set;
        }
        public string TicketId { get; set; } = default!;
        public string? AuthorUserId
        {
            get; set;
        }           // кто написал (если user)
        public string AuthorRole { get; set; } = "user";    // user | agent
        public string Body { get; set; } = default!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReminderEmailSentAt
        {
            get; set;
        }
        public SupportTicket? Ticket
        {
            get; set;
        }
    }
}
