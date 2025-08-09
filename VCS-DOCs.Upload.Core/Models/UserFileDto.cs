namespace VCS_DOCs.Upload.Core.Models
{
    public class UserFileDto
    {
        public Guid FileId
        {
            get; set;
        }
        public string FileName { get; set; } = "";
        public long FileSize
        {
            get; set;
        }

        // IMPORTANT: DateTimeOffset to preserve UTC offset in JSON
        public DateTimeOffset UpdatedAt
        {
            get; set;
        }

        public int LatestVersion
        {
            get; set;
        }
        public List<VersionDto> Versions { get; set; } = new();
        public Guid FileGroupId
        {
            get; set;
        }
    }

    public class VersionDto
    {
        public int Version
        {
            get; set;
        }

        // IMPORTANT: DateTimeOffset to preserve UTC offset in JSON
        public DateTimeOffset UploadedAt
        {
            get; set;
        }

        public long FileSize
        {
            get; set;
        }
    }
}