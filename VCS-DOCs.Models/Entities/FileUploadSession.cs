namespace VCS_DOCs.Models.Entities
{
	public class FileUploadSession
	{
		public Guid Id { get; set; } = Guid.NewGuid();
		public Guid FileId { get; set; } = Guid.NewGuid();

		public string UserId { get; set; } = null!;
		public string OriginalFileName { get; set; } = null!;
		public string FileHash { get; set; } = null!;

		public long FileSize { get; set; }
		public int TotalChunks { get; set; }
		public string Status { get; set; } = "pending";

		public DateTime CreatedAt { get; set; } = DateTime.Now;
		public DateTime UpdatedAt { get; set; } = DateTime.Now;

		public List<FileUploadChunk> Chunks { get; set; } = new();

		public int Version { get; set; } = 1;
		public bool IsLatest { get; set; } = false;
	}
}
