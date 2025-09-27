using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace VCS_DOCs.Upload.Core.Services.Antivirus
{
    /// <summary>
    /// Композит над несколькими IAntivirusScanner без полной буферизации огромных потоков.
    /// - seekable поток: просто сбрасываем Position=0 между сканерами;
    /// - non-seekable поток: делаем ограниченный spill в avTempDir (MaxSpillMb) или sample-скан (первые/последние N МБ).
    /// </summary>
    public sealed class CompositeScanner : IAntivirusScanner
    {
        private readonly IReadOnlyList<IAntivirusScanner> _scanners;
        private readonly string _avTempDir;
        private readonly long _maxSpillBytes;
        private readonly bool _enableSample;
        private readonly long _sampleHeadBytes;
        private readonly long _sampleTailBytes;

        // === Перегрузка: temp-папка + список сканеров (совместимо с твоим DI) ===
        public CompositeScanner(string avTempDir, params IAntivirusScanner[] scanners)
        {
            _scanners = scanners ?? Array.Empty<IAntivirusScanner>();
            _avTempDir = string.IsNullOrWhiteSpace(avTempDir)
                ? Path.Combine(AppContext.BaseDirectory, "storage", "userData", "_tmp", "av")
                : avTempDir;
            Directory.CreateDirectory(_avTempDir);

            _maxSpillBytes = 64L * 1024 * 1024; // 64 MB по умолчанию
            _enableSample = true;
            _sampleHeadBytes = 8L * 1024 * 1024; // 8 MB
            _sampleTailBytes = 8L * 1024 * 1024; // 8 MB
        }

        // === Перегрузка: читаем настройки из IConfiguration ===
        public CompositeScanner(IConfiguration cfg, params IAntivirusScanner[] scanners)
        {
            _scanners = scanners ?? Array.Empty<IAntivirusScanner>();

            _avTempDir = cfg["Antivirus:TempDir"]
                         ?? Path.Combine(AppContext.BaseDirectory, "storage", "userData", "_tmp", "av");
            Directory.CreateDirectory(_avTempDir);

            _maxSpillBytes = CfgInt(cfg, "Antivirus:MaxSpillMb", 64) * 1024L * 1024L;
            _enableSample = CfgBool(cfg, "Antivirus:EnableSampleScan", true);
            _sampleHeadBytes = CfgInt(cfg, "Antivirus:SampleHeadMb", 8) * 1024L * 1024L;
            _sampleTailBytes = CfgInt(cfg, "Antivirus:SampleTailMb", 8) * 1024L * 1024L;
        }

        // === Перегрузка: всё по умолчанию ===
        public CompositeScanner(params IAntivirusScanner[] scanners)
            : this(Path.Combine(AppContext.BaseDirectory, "storage", "userData", "_tmp", "av"), scanners)
        {
        }

        private static bool CfgBool(IConfiguration cfg, string key, bool def)
        {
            try
            {
                var s = cfg[key];
                if (bool.TryParse(s, out var b)) return b;
                if (int.TryParse(s, out var i)) return i != 0;
                return def;
            }
            catch { return def; }
        }

        private static int CfgInt(IConfiguration cfg, string key, int def)
        {
            try { return int.TryParse(cfg[key], out var i) ? i : def; }
            catch { return def; }
        }

        public async Task<ScanVerdict> ScanAsync(Stream content, string? contentName = null, CancellationToken ct = default)
        {
            // Сначала sample view (если включено и имеет смысл)
            if (_enableSample && TryBuildSampleView(content, out var sampled))
            {
                using (sampled) { return await ScanSeekableOrSampledAsync(sampled, contentName, ct); }
            }

            if (content.CanSeek)
            {
                return await ScanSeekableOrSampledAsync(content, contentName, ct);
            }

            // non-seekable: создаём ограниченный spill; если превышает лимит — Unavailable
            if (!TryCreateLimitedSpill(content, _maxSpillBytes, out var spillPath, out var spillStream))
            {
                TryDeleteQuiet(spillPath);
                return ScanVerdict.Unavailable;
            }

            try
            {
                return await ScanSeekableOrSampledAsync(spillStream!, contentName, ct);
            }
            finally
            {
                try { spillStream?.Dispose(); } catch { }
                TryDeleteQuiet(spillPath);
            }
        }

        private async Task<ScanVerdict> ScanSeekableOrSampledAsync(Stream seekable, string? name, CancellationToken ct)
        {
            foreach (var s in _scanners)
            {
                ct.ThrowIfCancellationRequested();
                if (seekable.CanSeek) seekable.Position = 0;
                var v = await s.ScanAsync(seekable, name, ct);
                if (v == ScanVerdict.Infected) return ScanVerdict.Infected;
                if (v == ScanVerdict.Error) return ScanVerdict.Error;
                // Unavailable — пробуем следующий
            }
            return ScanVerdict.Clean;
        }

        private bool TryBuildSampleView(Stream src, out Stream view)
        {
            view = src;
            try
            {
                if (!src.CanSeek) return false;
                var len = src.Length;
                if (len <= 0) return false;

                var head = Math.Min(_sampleHeadBytes, len);
                var tail = Math.Min(_sampleTailBytes, Math.Max(0, len - head));
                if (head + tail <= 0 || head + tail >= len) return false; // бессмысленно: почти весь файл

                view = new HeadTailCompositeStream(src, head, tail);
                return true;
            }
            catch { return false; }
        }

        private bool TryCreateLimitedSpill(Stream src, long maxSpillBytes, out string? tmpPath, out FileStream? spill)
        {
            tmpPath = null;
            spill = null;
            try
            {
                Directory.CreateDirectory(_avTempDir);
                tmpPath = Path.Combine(_avTempDir, $"spill_{Guid.NewGuid():N}.bin");

                long copied = 0;
                spill = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
                    bufferSize: 1024 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

                var buffer = new byte[1024 * 1024];
                int r;
                while ((r = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    copied += r;
                    if (copied > maxSpillBytes)
                    {
                        spill.Dispose();
                        TryDeleteQuiet(tmpPath);
                        spill = null; tmpPath = null;
                        return false;
                    }
                    spill.Write(buffer, 0, r);
                }

                spill.Position = 0;
                return true;
            }
            catch
            {
                try { spill?.Dispose(); } catch { }
                TryDeleteQuiet(tmpPath);
                spill = null; tmpPath = null;
                return false;
            }
        }

        private static void TryDeleteQuiet(string? path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); } catch { }
        }

        /// <summary>
        /// Поток-«вид» head+tail одного источника (без копирования всего файла).
        /// </summary>
        private sealed class HeadTailCompositeStream : Stream
        {
            private readonly Stream _src;
            private readonly long _headLen;
            private readonly long _tailLen;
            private readonly long _tailStart;
            private long _pos;

            public HeadTailCompositeStream(Stream src, long headLen, long tailLen)
            {
                _src = src ?? throw new ArgumentNullException(nameof(src));
                if (!src.CanSeek) throw new InvalidOperationException("Source must be seekable");
                _headLen = headLen;
                _tailLen = tailLen;
                _tailStart = Math.Max(0, _src.Length - tailLen);
                _pos = 0;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _headLen + _tailLen;
            public override long Position
            {
                get => _pos; set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_pos >= Length) return 0;
                int total = 0;

                // head
                if (_pos < _headLen)
                {
                    _src.Position = _pos;
                    int toRead = (int)Math.Min(count, _headLen - _pos);
                    int r = _src.Read(buffer, offset, toRead);
                    _pos += r; total += r; offset += r; count -= r;
                    if (count <= 0) return total;
                }

                // tail
                if (_pos >= _headLen && _pos < Length && count > 0)
                {
                    long withinTail = _pos - _headLen;
                    _src.Position = _tailStart + withinTail;
                    int toRead = (int)Math.Min(count, Length - _pos);
                    int r = _src.Read(buffer, offset, toRead);
                    _pos += r; total += r;
                }

                return total;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => Task.FromResult(Read(buffer, offset, count));

            protected override void Dispose(bool disposing)
            { /* view — исходник не закрываем */
            }

            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
