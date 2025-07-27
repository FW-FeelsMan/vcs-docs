using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

public static class RamDiskManager
{
	public static string? RamDriveLetter { get; private set; }

	public static string GetRamDiskPath() => $"{RamDriveLetter}:\\";

	private static char? FindFreeDriveLetter()
	{
		var drives = DriveInfo.GetDrives().Select(d => char.ToUpper(d.Name[0])).ToHashSet();
		return Enumerable.Range('R', 'Z' - 'R' + 1)
						 .Select(i => (char)i)
						 .FirstOrDefault(letter => !drives.Contains(letter));
	}

	public static bool InitializeRamDisk(int sizeGb)
	{
		if (sizeGb <= 0)
			return false;

		var freeLetter = FindFreeDriveLetter();
		if (freeLetter == default)
			throw new InvalidOperationException("Нет свободных букв дисков для RAM-диска.");

		RamDriveLetter = freeLetter.ToString();

		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "imdisk",
				Arguments = $"-a -s {sizeGb}G -m {RamDriveLetter}: -p \"/fs:ntfs /q /y\" -o rw,rem",
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}
		};

		process.Start();
		process.WaitForExit();

		if (process.ExitCode == 0)
		{
			SetDriveLabel($"{RamDriveLetter}:", "VCS-DOCs.Ram-disk");
			return true;
		}

		return false;
	}

	private static void SetDriveLabel(string driveLetter, string label)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = "label",
			Arguments = $"{driveLetter} {label}",
			CreateNoWindow = true,
			UseShellExecute = false,
		})?.WaitForExit();
	}

	public static async Task CleanupAsync()
	{
		if (string.IsNullOrEmpty(RamDriveLetter)) return;

		var process = new Process
		{
			StartInfo = new ProcessStartInfo
			{
				FileName = "imdisk.exe",
				Arguments = $"-D -m {RamDriveLetter}:",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			}
		};

		process.Start();
		await process.WaitForExitAsync();

		//Console.WriteLine($"[RAM-DISK CLEANUP] Диск {RamDriveLetter} успешно размонтирован.");
	}

}