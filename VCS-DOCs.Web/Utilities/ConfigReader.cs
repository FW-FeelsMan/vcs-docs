using System.Text;

namespace VCS_DOCs.Utilities;

public static class ConfigReader
{
	private const string Key = "Speciality";

	public static List<string> GetSpecialities(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
			return new List<string> { "Нет доступных специальностей" };

		var result = new List<string>();

		try
		{
			foreach (var line in File.ReadLines(filePath, Encoding.UTF8))
			{
				if (string.IsNullOrWhiteSpace(line))
					continue;

				var trimmed = line.Trim();
				if (trimmed.StartsWith(';') || trimmed.StartsWith('#'))
					continue;

				if (!trimmed.StartsWith(Key, StringComparison.OrdinalIgnoreCase))
					continue;

				var eq = trimmed.IndexOf('=');
				if (eq <= 0 || eq == trimmed.Length - 1)
					continue;

				var value = trimmed[(eq + 1)..].Trim();
				if (!string.IsNullOrWhiteSpace(value))
					result.Add(value);
			}
		}
		catch
		{
			return new List<string> { "Нет доступных специальностей" };
		}

		return result.Count > 0 ? result : new List<string> { "Нет доступных специальностей" };
	}
}
