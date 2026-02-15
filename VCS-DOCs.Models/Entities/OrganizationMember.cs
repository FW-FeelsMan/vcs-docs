namespace VCS_DOCs.Models.Entities
{
	public class OrganizationMember
	{
		public string OrganizationId { get; set; } = string.Empty;
		public string UserId { get; set; } = string.Empty;
		public OrganizationMemberRole Role { get; set; } = OrganizationMemberRole.Owner;
		public string? Position { get; set; }
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

		public Organization? Organization { get; set; }
		public User? User { get; set; }
	}
}
