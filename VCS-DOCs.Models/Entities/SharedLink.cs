using System;
using System.ComponentModel.DataAnnotations;

namespace VCS_DOCs.Models.Entities
{
    public class SharedLink
    {
        [Key]
        public Guid Id
        {
            get; set;
        }

        public Guid FileGroupId
        {
            get; set;
        }
        public int Version
        {
            get; set;
        }

        /// <summary>Unix seconds (UTC) until which link is valid.</summary>
        public long Exp
        {
            get; set;
        }

        /// <summary>Optional limit of total downloads.</summary>
        public int? MaxDownloads
        {
            get; set;
        }

        /// <summary>Current number of downloads performed.</summary>
        public int Downloads
        {
            get; set;
        }

        /// <summary>If true - only authenticated users (owner or not) may use the link.</summary>
        public bool RequireAuth
        {
            get; set;
        }

        public string CreatedBy { get; set; } = "";
        public DateTime CreatedAt
        {
            get; set;
        }
    }
}