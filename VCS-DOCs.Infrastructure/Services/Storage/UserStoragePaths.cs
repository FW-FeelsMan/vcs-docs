public class UserStoragePaths
{
	private readonly string _basePath;

	public UserStoragePaths(string basePath)
	{
		_basePath = basePath;
	}

	public string GetUserRoot(string userIdShort) =>
		Path.Combine(_basePath, $"u_{userIdShort}");

	public string GetAvatarPath(string userIdShort) =>
		Path.Combine(GetUserRoot(userIdShort), "a", "avatar.jpg");

	public string GetChunkPath(string userIdShort, Guid sessionId, int chunkIndex) =>
		Path.Combine(GetUserRoot(userIdShort), "chunks", sessionId.ToString(), $"chunk_{chunkIndex}");

	public string GetChunkFolder(string userIdShort, Guid sessionId) =>
		Path.Combine(GetUserRoot(userIdShort), "chunks", sessionId.ToString());

	public string GetChunkHashJsonTempPath(string userIdShort, Guid sessionId) =>
		Path.Combine(GetChunkFolder(userIdShort, sessionId), "chunks.json");

	public string GetChunkHashJsonFinalPath(string userIdShort, string fileHash, int version) =>
		Path.Combine(GetVersionedFileFolder(userIdShort, fileHash, version), "chunks.json");

	public string GetFileRoot(string userIdShort) =>
		Path.Combine(GetUserRoot(userIdShort), "files");
    public string BaseStoragePath => _basePath;

    public string GetFilePath(string userIdShort, string fileHash, int version, string fileName) =>
		Path.Combine(GetFileRoot(userIdShort), fileHash, $"v{version}", fileName);

	public string GetVersionedFileFolder(string userIdShort, string fileHash, int version) =>
		Path.Combine(GetFileRoot(userIdShort), fileHash, $"v{version}");

	public string GetMetaJsonPath(string userIdShort, string fileHash, int version) =>
		Path.Combine(GetVersionedFileFolder(userIdShort, fileHash, version), "meta.json");
}