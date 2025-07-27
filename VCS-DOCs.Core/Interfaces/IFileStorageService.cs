public interface IFileStorageService
{
    Task<byte[]> ReadFileAsync(string userIdShort, Guid fileId, int version, string fileName);
    Task DeleteFileAsync(string userIdShort, Guid fileId, int version, string fileName);
    Task SaveFileAsync(string userIdShort, Guid fileId, int version, string fileName, Stream content);
    Task<long> GetUsedBytesAsync(string shortUserId);
    Task<long> GetTempBytesAsync(string shortUserId);
    string GetBasePath();
}
