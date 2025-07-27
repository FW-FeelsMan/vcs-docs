namespace VCS_DOCs.TaskEngine
{
	public class TaskResult
	{
		public bool Success { get; set; }
		public string Message { get; set; } = "";
		public object? Data { get; set; }
	}
}