namespace VCS_DOCs.Upload.Core.Services.Antivirus
{
    public enum ScanVerdict
    {
        Clean,
        Infected,
        Unavailable,
        Error
    }

    public interface IAntivirusScanner
    {
        Task<ScanVerdict> ScanAsync(Stream content, string? contentName = null, CancellationToken ct = default);
    }
}