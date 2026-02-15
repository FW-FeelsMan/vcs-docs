namespace VCS_DOCs.Models.Entities
{
	public class Organization
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("D");
		public string Name { get; set; } = string.Empty;
		public string Inn { get; set; } = string.Empty;
		public string Email { get; set; } = string.Empty;
		public string Country { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
		public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
		public bool IsDeleted { get; set; }

		public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
	}
}
