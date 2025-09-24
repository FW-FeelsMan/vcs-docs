using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace VCS_DOCs.TaskEngine
{
    public interface ITaskModule
    {
        string Id
        {
            get;
        }                 // уникальный id модуля
        string Name
        {
            get;
        }               // человекочитаемое имя
        TimeSpan RunEvery
        {
            get;
        }         // период запуска (минимум планировщика)

        Task InitAsync(IServiceProvider services, IConfiguration cfg, ILogger logger, CancellationToken ct);
        Task<TaskResult> ExecuteAsync(TaskContext ctx, CancellationToken ct);
    }
}
