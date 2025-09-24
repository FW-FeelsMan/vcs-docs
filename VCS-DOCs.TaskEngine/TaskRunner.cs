using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Runtime.Loader;
using System.Threading.Channels;

namespace VCS_DOCs.TaskEngine
{
    public sealed class TaskRunner : BackgroundService
    {
        private readonly IServiceProvider _root;
        private readonly IConfiguration _cfg;
        private readonly ILogger<TaskRunner> _log;
        private readonly TaskHostOptions _opt;

        private readonly List<ModuleDescriptor> _modules = new();
        private readonly SemaphoreSlim _concurrency;
        private readonly Channel<Func<CancellationToken, Task>> _work;

        public TaskRunner(IServiceProvider root, IConfiguration cfg, ILogger<TaskRunner> log, TaskHostOptions opt)
        {
            _root = root;
            _cfg = cfg;
            _log = log;
            _opt = opt;

            _concurrency = new SemaphoreSlim(Math.Max(1, _opt.MaxConcurrency));
            _work = Channel.CreateUnbounded<Func<CancellationToken, Task>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await LoadModulesAsync(stoppingToken);

            // отдельный consumer очереди (параллелизм регулируем семафором)
            var consumer = Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

            var tick = TimeSpan.FromSeconds(Math.Max(1, _opt.ScanPeriodSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var md in _modules)
                    {
                        if (now >= md.NextRunUtc)
                        {
                            // планируем запуск
                            var module = md.Instance;
                            md.NextRunUtc = now + module.RunEvery; // поставить след. запуск

                            await _work.Writer.WriteAsync(async ct =>
                            {
                                await _concurrency.WaitAsync(ct);
                                try
                                {
                                    using var scope = _root.CreateScope();
                                    var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
                                    var logger = loggerFactory.CreateLogger($"TaskModule.{module.Id}");

                                    await module.InitAsync(scope.ServiceProvider, _cfg, logger, ct);

                                    var ctx = new TaskContext
                                    {
                                        TaskId = $"{module.Id}:{now:yyyyMMddHHmmss}",
                                        UserId = "system",
                                        Parameters = new()
                                    };

                                    var res = await module.ExecuteAsync(ctx, ct);

                                    logger.LogInformation("Module {Id} finished: success={Success} msg={Msg}",
                                        module.Id, res.Success, res.Message);
                                }
                                catch (Exception ex)
                                {
                                    _log.LogError(ex, "Module {Id} failed", module.Id);
                                    if (_opt.ThrowOnModuleError) throw;
                                }
                                finally
                                {
                                    _concurrency.Release();
                                }
                            }, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Scheduler tick failed");
                }

                try { await Task.Delay(tick, stoppingToken); } catch { }
            }

            _work.Writer.Complete();
            await consumer;
        }

        private async Task ConsumeLoop(CancellationToken ct)
        {
            await foreach (var job in _work.Reader.ReadAllAsync(ct))
            {
                _ = Task.Run(() => job(ct), ct);
            }
        }

        private async Task LoadModulesAsync(CancellationToken ct)
        {
            var dir = Path.IsPathRooted(_opt.ModulesPath)
                ? _opt.ModulesPath
                : Path.Combine(AppContext.BaseDirectory, _opt.ModulesPath);

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _log.LogInformation("Loading modules from {Dir}", dir);
            foreach (var file in Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var alc = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(file), isCollectible: false);
                    await using var fs = File.OpenRead(file);
                    var asm = alc.LoadFromStream(fs);

                    var types = asm.GetTypes()
                        .Where(t => typeof(ITaskModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                        .ToArray();

                    foreach (var t in types)
                    {
                        var inst = (ITaskModule)ActivatorUtilities.CreateInstance(_root, t);
                        _modules.Add(new ModuleDescriptor
                        {
                            Instance = inst,
                            NextRunUtc = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2) // первый запуск чуть позже
                        });

                        _log.LogInformation("Loaded module {Id} ({Name}) from {Dll}", inst.Id, inst.Name, Path.GetFileName(file));
                    }
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Failed to load modules from {Dll}", file);
                }
            }

            if (_modules.Count == 0)
                _log.LogWarning("No modules found in {Dir}", dir);
        }
    }
}