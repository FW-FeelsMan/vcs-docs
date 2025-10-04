using System.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace VCS_DOCs.Support.Monitoring
{
    /// <summary>
    /// Middleware: измеряет время ответа, статус, байты (in/out) и пишет в WorkloadStore.AddRequest().
    /// </summary>
    public sealed class EndpointStatsCollector
    {
        private readonly RequestDelegate _next;
        private readonly WorkloadStore _store;

        private static readonly PathString[] _skipPrefixes =
        {
            "/css", "/js", "/lib", "/img", "/images", "/favicon", "/fonts", "/static", "/hubs"
        };

        public EndpointStatsCollector(RequestDelegate next, WorkloadStore store)
        {
            _next = next;
            _store = store;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            var path = ctx.Request.Path;

            // пропускаем статик/вепсокеты/хабы
            if (_skipPrefixes.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
            {
                await _next(ctx);
                return;
            }

            var reqBytes = ctx.Request.ContentLength ?? 0L;

            // обернём Body, чтобы посчитать исходящие байты
            var originalBody = ctx.Response.Body;
            await using var counting = new CountingWriteStream(originalBody);
            ctx.Response.Body = counting;

            var sw = Stopwatch.StartNew();
            var status = 0;

            try
            {
                await _next(ctx);
                status = ctx.Response?.StatusCode ?? 0;
            }
            catch
            {
                status = 500; // чтобы метрика не потерялась
                throw;
            }
            finally
            {
                sw.Stop();
                try { await ctx.Response.Body.FlushAsync(); } catch { /* ignore */ }
                var respBytes = counting.BytesWritten;

                // вернуть оригинальный stream
                ctx.Response.Body = originalBody;

                var route = TryGetRouteTemplate(ctx) ?? path.ToString();
                route = NormalizeRoute(route);

                // основная запись метрики
                _store.AddRequest(
                    routeKey: route,
                    statusCode: status,
                    durMs: sw.Elapsed.TotalMilliseconds,
                    bytesIn: reqBytes,
                    bytesOut: respBytes
                );
            }
        }

        private static string? TryGetRouteTemplate(HttpContext ctx)
        {
            var ep = ctx.GetEndpoint();
            if (ep is RouteEndpoint re)
                return re.RoutePattern?.RawText;
            return ep?.DisplayName;
        }

        private static string NormalizeRoute(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "/";
            // чуть подчистим длинные DisplayName вида "...Controller.Action (Assembly)"
            if (s.Contains("Controller", StringComparison.OrdinalIgnoreCase) && s.Contains("("))
            {
                var i = s.IndexOf('(');
                if (i > 0) s = s[..i].Trim();
                s = s.Replace("Controllers.", "").Replace("Controller.", "")
                     .Replace("Controller", "").Replace("  ", " ");
            }
            return s;
        }

        /// <summary>обёртка над Response.Body, считаем байты записи и прокидываем дальше без буферизации</summary>
        private sealed class CountingWriteStream : Stream
        {
            private readonly Stream _inner;
            public long BytesWritten
            {
                get; private set;
            }

            public CountingWriteStream(Stream inner) => _inner = inner;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position
            {
                get => _inner.Position; set => _inner.Position = value;
            }

            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count > 0) BytesWritten += count;
                _inner.Write(buffer, offset, count);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (buffer.Length > 0) BytesWritten += buffer.Length;
                await _inner.WriteAsync(buffer, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (count > 0) BytesWritten += count;
                return _inner.WriteAsync(buffer, offset, count, cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                // Не закрываем _inner — это реальный Response.Body
                base.Dispose(disposing);
            }

            public override ValueTask DisposeAsync()
            {
                // Не закрываем _inner — только обёртка
                return ValueTask.CompletedTask;
            }
        }
    }
}
