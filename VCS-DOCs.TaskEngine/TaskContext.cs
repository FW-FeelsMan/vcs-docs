using System.Text.Json;

namespace VCS_DOCs.TaskEngine
{
	public class TaskContext
	{
		public string UserId { get; set; } = "";
		public string TaskId { get; set; } = "";
		public Dictionary<string, object> Parameters { get; set; } = new();

		public static TaskContext FromJson(string json)
		{
			return JsonSerializer.Deserialize<TaskContext>(json)
				   ?? throw new InvalidOperationException("Ошибка десериализации контекста таски");
		}
	}
}