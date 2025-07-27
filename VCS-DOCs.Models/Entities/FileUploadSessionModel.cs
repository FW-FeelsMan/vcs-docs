using System.ComponentModel.DataAnnotations;

namespace VCS_DOCs.Models.Entities
{
	public class FileUploadSessionModel
	{
		[Key]
		public Guid FileId { get; set; }
		public string UserId { get; set; } = "";
		public string OriginalFileName { get; set; } = "";
		public string FileHash { get; set; } = "";
		public long FileSize { get; set; }
		public string Status { get; set; } = "pending";
		public DateTime UpdatedAt { get; set; }
		public int Version { get; set; }
        public Guid FileGroupId
        {
            get; set;
        }
    }
}
