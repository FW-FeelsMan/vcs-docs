using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VCS_DOCs.TaskEngine
{
    public static class TaskEngineServiceCollectionExtensions
    {
        public static IServiceCollection AddTaskEngine(this IServiceCollection services, IConfiguration cfg)
        {
            var opt = new TaskHostOptions();
            cfg.GetSection("TaskEngine").Bind(opt);

            services.AddSingleton(opt);
            services.AddHostedService<TaskRunner>();
            return services;
        }
    }
}