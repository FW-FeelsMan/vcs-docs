using Microsoft.AspNetCore.Identity;

namespace VCS_DOCs.Models.Entities
{
	public class User : IdentityUser
	{
		public string FullName { get; set; } = "Не установлено";
		public string DateOfBirth { get; set; } = "Не установлено";
		public string Organization { get; set; } = "Не установлено";
		public string Department { get; set; } = "Не установлено";
		public string? Speciality { get; set; }
		public int StatusOnline { get; set; }
		public string? HardwareId { get; set; }
		public DateTime? LastEntry { get; set; }
		public DateTime CreatedAt { get; set; }
		public DateTime UpdatedAt { get; set; }
		public int Access { get; set; }
		public string? JwtId { get; set; }
		public bool IsDeleted { get; set; } = false;
		public long? StorageLimitBytes { get; set; }
	}
}
