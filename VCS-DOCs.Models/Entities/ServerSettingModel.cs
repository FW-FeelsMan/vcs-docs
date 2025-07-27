using System.ComponentModel.DataAnnotations;

namespace VCS_DOCs.Models.Entities
{
	public class ServerSettingModel
	{
		[Key]
		public string Key { get; set; } = default!;
		public string Value { get; set; } = default!;
	}
}