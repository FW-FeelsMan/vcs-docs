namespace VCS_DOCs.Upload.Core.Models
{
	public class FileContentResultModel
	{
		public byte[] Content { get; set; } = Array.Empty<byte>();
		public string FileName { get; set; } = "";
	}
}
