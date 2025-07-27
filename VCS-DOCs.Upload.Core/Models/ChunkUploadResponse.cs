namespace VCS_DOCs.Upload.Core.Models
{
	public class ChunkUploadResponse
	{
		public Guid SessionId { get; set; }
		public string Message { get; set; } = "OK";
	}
}