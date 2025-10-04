using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VCS_DOCs.Support.Monitoring
{
    /// <summary>
    /// Таймер на 5 секунд, который дёргает WorkloadStore.Tick5s().
    /// Внутри Tick5s считаются CPU/RAM и конструируются ряды.
    /// </summary>
    public sealed class WorkloadSampler : BackgroundService
    {
        private readonly ILogger<WorkloadSampler> _log;
        private readonly WorkloadStore _store;

        public WorkloadSampler(ILogger<WorkloadSampler> log, WorkloadStore store)
        {
            _log = log;
            _store = store;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _log.LogInformation("WorkloadSampler started (tick=5s).");

            // первый «пустой» тик, чтобы фронт сразу получил данные
            SafeTick();

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    SafeTick();
                }
            }
            catch (OperationCanceledException) { /* normal stop */ }

            _log.LogInformation("WorkloadSampler stopped.");
        }

        private void SafeTick()
        {
            try { _store.Tick5s(); }
            catch (Exception ex) { _log.LogDebug(ex, "Tick5s failed"); }
        }
    }
}
