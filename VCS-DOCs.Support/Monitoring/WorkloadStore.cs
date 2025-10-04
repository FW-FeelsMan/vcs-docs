using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VCS_DOCs.Infrastructure.Data;
using VCS_DOCs.Infrastructure.Data;

namespace VCS_DOCs.Support.Monitoring;

public sealed class WorkloadStore
{
    // === Конфиг/DI ===
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;

    private readonly IServiceScopeFactory _scopeFactory;

    public WorkloadStore(IConfiguration cfg, IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory)
    {
        _cfg = cfg;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _proc = Process.GetCurrentProcess();
        _lastCpu = _proc.TotalProcessorTime;
        _lastCpuAt = DateTime.UtcNow;
    }


    // === Счётчики, которые пополняет middleware ===
    private long _reqTotal;
    private long _errTotal;
    private long _bytesInTotal;
    private long _bytesOutTotal;

    public void AddRequest(string routeKey, int statusCode, double durMs, long bytesIn, long bytesOut)
    {
        Interlocked.Increment(ref _reqTotal);
        if (statusCode >= 400) Interlocked.Increment(ref _errTotal);
        if (bytesIn > 0) Interlocked.Add(ref _bytesInTotal, bytesIn);
        if (bytesOut > 0) Interlocked.Add(ref _bytesOutTotal, bytesOut);

        var ep = _endpoints.GetOrAdd(routeKey, _ => new EndpointStats());
        ep.Add(statusCode, durMs);
    }

    // === Эндпоинты/агрегация ===
    private sealed class EndpointStats
    {
        private readonly object _lock = new();
        private readonly Queue<double> _dur = new();         // последние N длительностей
        private const int N = 512;
        private readonly long[] _bucketStamp = new long[60];  // секунды UNIX
        private readonly int[] _bucketCount = new int[60];
        private readonly int[] _bucketErr = new int[60];

        public void Add(int status, double durMs)
        {
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var idx = (int)(nowSec % 60);

            lock (_lock)
            {
                if (_bucketStamp[idx] != nowSec) { _bucketStamp[idx] = nowSec; _bucketCount[idx] = 0; _bucketErr[idx] = 0; }
                _bucketCount[idx]++; if (status >= 400) _bucketErr[idx]++;

                _dur.Enqueue(durMs);
                while (_dur.Count > N) _dur.Dequeue();
            }
        }

        public (double avgMs, double p95Ms, double rps, double errPct) Snapshot()
        {
            lock (_lock)
            {
                // среднее/р95
                double avg = 0;
                if (_dur.Count > 0) avg = _dur.Average();
                double p95 = 0;
                if (_dur.Count > 0)
                {
                    var arr = _dur.ToArray();
                    Array.Sort(arr);
                    p95 = arr[(int)Math.Clamp(Math.Ceiling(arr.Length * 0.95) - 1, 0, arr.Length - 1)];
                }

                // за последнюю минуту
                var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                int sum = 0, err = 0;
                for (int i = 0; i < 60; i++)
                {
                    if (nowSec - _bucketStamp[i] < 60)
                    {
                        sum += _bucketCount[i];
                        err += _bucketErr[i];
                    }
                }

                double rps = sum / 60.0;
                double errPct = sum > 0 ? (double)err / sum * 100.0 : 0.0;
                return (avg, p95, rps, errPct);
            }
        }
    }

    private readonly ConcurrentDictionary<string, EndpointStats> _endpoints = new();

    // === Процесс/CPU/RAM ===
    private readonly Process _proc;
    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt;

    private static (double usedPct, long usedMb, long totalMb) GetMemory()
    {
        // GC total available — кроссплатформенный ориентир
        var gi = GC.GetGCMemoryInfo();
        var total = gi.TotalAvailableMemoryBytes > 0 ? gi.TotalAvailableMemoryBytes : (long)Environment.WorkingSet;
        var used = Process.GetCurrentProcess().WorkingSet64;
        double pct = total > 0 ? (double)used / total * 100.0 : 0.0;
        return (pct, used / (1024 * 1024), total / (1024 * 1024));
    }

    private double SampleCpuPercent()
    {
        var now = DateTime.UtcNow;
        var cpu = _proc.TotalProcessorTime;
        var dCpu = (cpu - _lastCpu).TotalMilliseconds;
        var dMs = (now - _lastCpuAt).TotalMilliseconds;
        _lastCpu = cpu; _lastCpuAt = now;
        if (dMs <= 0) return 0;
        var cores = Math.Max(1, Environment.ProcessorCount);
        return Math.Clamp(dCpu / (dMs * cores) * 100.0, 0, 100);
    }

    // === Кольцевой буфер сэмплов (шаг 5с, глубина 24ч) ===
    private readonly object _bufLock = new();
    private const int STEP_SEC = 5;
    private const int CAPACITY = 24 * 60 * 60 / STEP_SEC; // 17280
    private readonly Sample[] _buf = new Sample[CAPACITY];
    private int _bufCount = 0, _bufIdx = 0;

    private struct Sample
    {
        public DateTime Ts;
        public double Cpu;     // %
        public double Ram;     // %
        public double Rps;     // per sec (за интервал)
        public double ErrPct;  // %
        public double NetIn;   // MB/s
        public double NetOut;  // MB/s
    }

    // totals → для дельт между тиками
    private long _lastReq, _lastErr, _lastIn, _lastOut;

    public void Tick5s()
    {
        var cpu = SampleCpuPercent();
        var (ramPct, _, _) = GetMemory();

        var reqNow = Interlocked.Read(ref _reqTotal);
        var errNow = Interlocked.Read(ref _errTotal);
        var inNow = Interlocked.Read(ref _bytesInTotal);
        var outNow = Interlocked.Read(ref _bytesOutTotal);

        var dReq = Math.Max(0, reqNow - _lastReq);
        var dErr = Math.Max(0, errNow - _lastErr);
        var dIn = Math.Max(0, inNow - _lastIn);
        var dOut = Math.Max(0, outNow - _lastOut);

        _lastReq = reqNow; _lastErr = errNow; _lastIn = inNow; _lastOut = outNow;

        var rps = dReq / (double)STEP_SEC;
        var errPct = dReq > 0 ? (double)dErr / dReq * 100.0 : 0.0;
        var mbIn = dIn / (1024.0 * 1024.0) / STEP_SEC;
        var mbOut = dOut / (1024.0 * 1024.0) / STEP_SEC;

        lock (_bufLock)
        {
            _buf[_bufIdx] = new Sample
            {
                Ts = DateTime.UtcNow,
                Cpu = cpu,
                Ram = ramPct,
                Rps = rps,
                ErrPct = errPct,
                NetIn = mbIn,
                NetOut = mbOut
            };
            _bufIdx = (_bufIdx + 1) % CAPACITY;
            _bufCount = Math.Min(_bufCount + 1, CAPACITY);
        }
    }

    public async Task<WorkloadSnapshot> BuildSnapshot(string range)
    {
        int group;      // сколько 5-сек сэмплов в 1 точке
        int points;
        string labelFmt;

        switch (range)
        {
            case "24h": group = 720; points = 24; labelFmt = "HH:mm"; break; // 60мин/5с
            case "1h": group = 60; points = 12; labelFmt = "HH:mm"; break; // 5мин
            default: group = 12; points = 15; labelFmt = "HH:mm"; break; // 1мин
        }

        Sample[] snap;
        lock (_bufLock)
        {
            var cnt = Math.Min(_bufCount, group * points);
            snap = new Sample[cnt];
            for (int i = 0; i < cnt; i++)
            {
                var idx = (_bufIdx - cnt + i);
                if (idx < 0) idx += CAPACITY;
                snap[i] = _buf[idx];
            }
        }

        var labels = new List<string>(points);
        var cpu = new List<double>(points);
        var ram = new List<double>(points);
        var rps = new List<double>(points);
        var err = new List<double>(points);
        var nin = new List<double>(points);
        var nout = new List<double>(points);

        DateTime? ts0 = snap.Length > 0 ? snap[0].Ts : DateTime.UtcNow;
        for (int i = 0; i < points; i++)
        {
            var seg = snap.Skip(Math.Max(0, snap.Length - (points - i) * group)).Take(group).ToArray();
            if (seg.Length == 0)
            {
                cpu.Add(0); ram.Add(0); rps.Add(0); err.Add(0); nin.Add(0); nout.Add(0);
                labels.Add(ts0!.Value.ToLocalTime().ToString(labelFmt));
                continue;
            }

            cpu.Add(seg.Average(s => s.Cpu));
            ram.Add(seg.Average(s => s.Ram));
            rps.Add(seg.Average(s => s.Rps));
            err.Add(seg.Average(s => s.ErrPct));
            nin.Add(seg.Average(s => s.NetIn));
            nout.Add(seg.Average(s => s.NetOut));

            var t = seg[0].Ts.ToLocalTime();
            labels.Add(t.ToString(labelFmt));
        }

        // KPI (последние 5 минут = 60 сэмплов по 5с)
        var last5m = snap.TakeLast(Math.Min(snap.Length, 60)).ToArray();
        double cpuAvg = last5m.Length > 0 ? last5m.Average(x => x.Cpu) : 0;
        double cpuP95 = last5m.Length > 0 ? Percentile(last5m.Select(x => x.Cpu), 0.95) : 0;
        var mem = GetMemory();
        double rpsAvg = last5m.Length > 0 ? last5m.Average(x => x.Rps) : 0;
        double rpsPeak = last5m.Length > 0 ? last5m.Max(x => x.Rps) : 0;
        double errAvg = last5m.Length > 0 ? last5m.Average(x => x.ErrPct) : 0;

        // таблица эндпоинтов
        var endpoints = _endpoints
            .Select(kv =>
            {
                var (avgMs, p95Ms, rpsE, errPctE) = kv.Value.Snapshot();
                return new EndpointRow
                {
                    Route = kv.Key,
                    AvgMs = Math.Round(avgMs, 0),
                    P95Ms = Math.Round(p95Ms, 0),
                    Rps = Math.Round(rpsE, 2),
                    ErrPct = Math.Round(errPctE, 2)
                };
            })
            .OrderByDescending(r => r.AvgMs)
            .Take(30)
            .ToList();

        // асинхронные части вне инициализатора
        var services = await GetServicesAsync();
        var queues = await GetQueuesAsync();

        return new WorkloadSnapshot
        {
            Range = range,
            Labels = labels,
            Series = new ChartSeries { Cpu = cpu, Ram = ram, Rps = rps, Err = err, NetIn = nin, NetOut = nout },
            Kpi = new KpiBlock
            {
                CpuAvg = Math.Round(cpuAvg, 0),
                CpuP95 = Math.Round(cpuP95, 0),
                RamUsedPct = Math.Round(mem.usedPct, 0),
                RamUsedMb = mem.usedMb,
                RamTotalMb = mem.totalMb,
                RpsAvg = Math.Round(rpsAvg, 1),
                RpsPeak = Math.Round(rpsPeak, 1),
                ErrRate = Math.Round(errAvg, 1)
            },
            Services = services,
            Queues = queues,
            Endpoints = endpoints
        };
    }

    private static double Percentile(IEnumerable<double> src, double p)
    {
        var arr = src.OrderBy(x => x).ToArray();
        if (arr.Length == 0) return 0;
        var k = (arr.Length - 1) * p;
        var f = Math.Floor(k);
        var c = Math.Ceiling(k);
        if (f == c) return arr[(int)k];
        return arr[(int)f] + (k - f) * (arr[(int)c] - arr[(int)f]);
    }

    // === Health/Очереди (минимально рабочие, кеш 30с) ===
    private DateTime _svcCacheAt = DateTime.MinValue;
    private List<ServiceRow> _svcCache = new();
    private DateTime _queueCacheAt = DateTime.MinValue;
    private List<QueueRow> _queueCache = new();

    private async Task<List<ServiceRow>> GetServicesAsync()
    {
        if ((DateTime.UtcNow - _svcCacheAt).TotalSeconds < 30) return _svcCache;
        var list = new List<ServiceRow>();

        // DB ping
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var t0 = Stopwatch.GetTimestamp();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            var ms = ElapsedMs(t0);
            list.Add(new ServiceRow("Db", "OK", $"{ms} ms"));
        }
        catch { list.Add(new ServiceRow("Db", "FAIL", "нет ответа")); }

        // Mail ping (TCP connect)
        try
        {
            var host = _cfg["Mail:Host"];
            var port = int.TryParse(_cfg["Mail:Port"], out var p) ? p : 25;
            var t0 = Stopwatch.GetTimestamp();
            using var tcp = new System.Net.Sockets.TcpClient();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            await tcp.ConnectAsync(host!, port, cts.Token);
            var ms = ElapsedMs(t0);
            list.Add(new ServiceRow("Mail", "OK", $"{ms} ms"));
        }
        catch { list.Add(new ServiceRow("Mail", "WARN", "нет коннекта")); }

        // VDocsBridge ping (GET /)
        try
        {
            var baseUrl = _cfg["VDocs:BaseUrl"] ?? _cfg["WebBaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                var t0 = Stopwatch.GetTimestamp();
                var http = _httpClientFactory.CreateClient("workload");
                using var resp = await http.GetAsync(baseUrl!, HttpCompletionOption.ResponseHeadersRead);
                var ms = ElapsedMs(t0);
                list.Add(new ServiceRow("VDocsBridge", resp.IsSuccessStatusCode ? "OK" : "WARN", $"{ms} ms"));
            }
        }
        catch { list.Add(new ServiceRow("VDocsBridge", "WARN", "ошибка")); }

        _svcCacheAt = DateTime.UtcNow;
        _svcCache = list;
        return list;
    }

    private static int ElapsedMs(long t0) => (int)(1000.0 * (Stopwatch.GetTimestamp() - t0) / Stopwatch.Frequency);

    private Task<List<QueueRow>> GetQueuesAsync()
    {
        // Если нет очередей — вернём пустой список (фронт отрисует «пусто»)
        if ((DateTime.UtcNow - _queueCacheAt).TotalSeconds < 15) return Task.FromResult(_queueCache);
        _queueCacheAt = DateTime.UtcNow;
        _queueCache = new();
        return Task.FromResult(_queueCache);
    }

    // === DTO для API ===
    public sealed class WorkloadSnapshot
    {
        public required string Range
        {
            get; init;
        }
        public required List<string> Labels
        {
            get; init;
        }
        public required ChartSeries Series
        {
            get; init;
        }
        public required KpiBlock Kpi
        {
            get; init;
        }
        public required List<ServiceRow> Services
        {
            get; init;
        }
        public required List<QueueRow> Queues
        {
            get; init;
        }
        public required List<EndpointRow> Endpoints
        {
            get; init;
        }
    }

    public sealed class ChartSeries
    {
        public required List<double> Cpu
        {
            get; init;
        }
        public required List<double> Ram
        {
            get; init;
        }
        public required List<double> Rps
        {
            get; init;
        }
        public required List<double> Err
        {
            get; init;
        }
        public required List<double> NetIn
        {
            get; init;
        }
        public required List<double> NetOut
        {
            get; init;
        }
    }

    public sealed class KpiBlock
    {
        public double CpuAvg
        {
            get; init;
        }
        public double CpuP95
        {
            get; init;
        }
        public double RamUsedPct
        {
            get; init;
        }
        public long RamUsedMb
        {
            get; init;
        }
        public long RamTotalMb
        {
            get; init;
        }
        public double RpsAvg
        {
            get; init;
        }
        public double RpsPeak
        {
            get; init;
        }
        public double ErrRate
        {
            get; init;
        }
    }

    public sealed record ServiceRow(string Name, string State, string Note);
    public sealed record QueueRow(string Name, int Depth, string Rate);
    public sealed class EndpointRow
    {
        public required string Route
        {
            get; init;
        }
        public double AvgMs
        {
            get; init;
        }
        public double P95Ms
        {
            get; init;
        }
        public double Rps
        {
            get; init;
        }
        public double ErrPct
        {
            get; init;
        }
    }
}
