namespace VCS_DOCs.Core.Interfaces
{
	public interface IFileStorageService
	{
		Task<byte[]> ReadFileAsync(string fileHash);
		Task DeleteFileAsync(string fileHash);
		Task SaveFileAsync(string fileHash, Stream content);
	}
}
