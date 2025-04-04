namespace VCS_DOCs.Services
{
	public class FileUploadTask
	{
		public string UserId { get; set; }
		public string DestinationFolder { get; set; }
		public string TempFilePath { get; set; }
		public string OriginalFileName { get; set; }
		public long FileLength { get; set; }
	}
}
