using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace VCS_DOCs.Upload.Core.Services.Antivirus
{
    // Очень лёгкий сканер на известные тест-сигнатуры (EICAR).
    public sealed class SimpleSignaturesScanner : IAntivirusScanner
    {
        private readonly bool _detectEicar;

        // Текст EICAR (ASCII)
        private static readonly byte[] EicarAscii = Encoding.ASCII.GetBytes(
            "X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");

        public SimpleSignaturesScanner(IConfiguration cfg)
        {
            // можно выключить в конфиге
            _detectEicar = string.Equals(cfg["Antivirus:Heuristics:DetectEicar"], "false", StringComparison.OrdinalIgnoreCase) ? false : true;
        }

        public async Task<ScanVerdict> ScanAsync(Stream content, string? contentName = null, CancellationToken ct = default)
        {
            if (!_detectEicar) return ScanVerdict.Clean;

            // маленькие файлы читаем целиком, большие - скользящим окном
            const int window = 4096;
            var buf = new byte[window * 2];
            int filled = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = await content.ReadAsync(buf, filled, buf.Length - filled, ct);
                if (read <= 0 && filled == 0) break;

                int len = filled + Math.Max(read, 0);
                if (ContainsSequence(buf, len, EicarAscii))
                    return ScanVerdict.Infected;

                if (read <= 0) break;

                // сдвигаем «хвост» окна на случай пересечения сигнатуры границей буферов
                var keep = Math.Min(EicarAscii.Length - 1, len);
                Buffer.BlockCopy(buf, len - keep, buf, 0, keep);
                filled = keep;
            }

            return ScanVerdict.Clean;
        }

        private static bool ContainsSequence(byte[] haystack, int count, byte[] needle)
        {
            if (needle.Length == 0 || count < needle.Length) return false;
            for (int i = 0; i <= count - needle.Length; i++)
            {
                int j = 0;
                for (; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) break;
                if (j == needle.Length) return true;
            }
            return false;
        }
    }
}
