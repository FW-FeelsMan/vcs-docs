namespace VCS_DOCs.TaskEngine
{
	public interface ITaskModule
	{
		string Name { get; }
		Task<TaskResult> ExecuteAsync(TaskContext context);
	}
}