namespace VCS_DOCs.Utilities
{
	public class FolderSizeHelper
	{
		private const long MaxFolderSizeBytes = 1L * 1024 * 1024 * 1024;

		public static long GetFolderSize(string folderPath)
		{
			if (!Directory.Exists(folderPath))
			{
				return 0;
			}

			return Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories)
							.Sum(file => new FileInfo(file).Length);
		}

		public static bool IsFolderSizeExceeded(string folderPath)
		{
			long currentSize = GetFolderSize(folderPath);
			return currentSize > MaxFolderSizeBytes;
		}

		public static void ClearFolder(string folderPath)
		{
			if (Directory.Exists(folderPath))
			{
				foreach (var file in Directory.GetFiles(folderPath))
				{
					File.Delete(file);
				}

				foreach (var subDirectory in Directory.GetDirectories(folderPath))
				{
					Directory.Delete(subDirectory, true);
				}
			}
		}

	}
}
