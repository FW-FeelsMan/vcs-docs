using System.ComponentModel.DataAnnotations;

namespace VCS_DOCs.Models.Entities
{
	public partial class FileUploadChunk
	{

		[Key]
		public Guid Id { get; set; } = Guid.NewGuid();

		public Guid SessionId { get; set; }
		public FileUploadSession Session { get; set; } = null!;

		public int Index { get; set; }
		public bool Uploaded { get; set; }
		public DateTime UpdatedAt { get; set; } = DateTime.Now;
	}
}
