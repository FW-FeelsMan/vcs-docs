using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VCS_DOCs.Upload.Core.Services.Antivirus
{
    public sealed class CompositeScanner : IAntivirusScanner
    {
        private readonly IReadOnlyList<IAntivirusScanner> _scanners;

        public CompositeScanner(params IAntivirusScanner[] scanners) => _scanners = scanners;

        public async Task<ScanVerdict> ScanAsync(Stream content, string? contentName = null, CancellationToken ct = default)
        {
            // читаем входной stream один раз: обернём в MemoryStream (разумно для небольших/средних файлов)
            // Для очень больших файлов у нас чанки — в момент сканирования это конкатенированный поток из файлов,
            // там можно сканировать напрямую: но тогда каждый сканер должен читать с начала.
            // Поэтому здесь делаем «буферизацию» в память только на последнем шаге получения всех чанков — файл уже на диске.
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            ms.Position = 0;

            var final = ScanVerdict.Clean;
            foreach (var s in _scanners)
            {
                ms.Position = 0;
                var v = await s.ScanAsync(ms, contentName, ct);
                if (v == ScanVerdict.Infected) return ScanVerdict.Infected;
                if (v == ScanVerdict.Error && final != ScanVerdict.Infected) final = ScanVerdict.Error;
                if (v == ScanVerdict.Unavailable && final == ScanVerdict.Clean) final = ScanVerdict.Unavailable;
            }
            return final;
        }
    }
}
