namespace VCS_DOCs.Models.Entities
{
    public class SupportTicket
    {
        public string Id { get; set; } = default!;          
        public string? Subject
        {
            get; set;
        }
        public string Status { get; set; } = "open";       
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt
        {
            get; set;
        }

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

        public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
    }
}
