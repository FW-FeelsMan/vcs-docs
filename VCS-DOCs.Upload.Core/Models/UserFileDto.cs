namespace VCS_DOCs.Upload.Core.Models
{
	public class UserFileDto
	{
		public Guid FileId { get; set; }
		public string FileName { get; set; } = "";
		public long FileSize { get; set; }
		public DateTime UpdatedAt { get; set; }

		public int LatestVersion { get; set; }
		public List<VersionDto> Versions { get; set; } = new();
	}

	public class VersionDto
	{
		public int Version { get; set; }
		public DateTime UploadedAt { get; set; }
	}
}
