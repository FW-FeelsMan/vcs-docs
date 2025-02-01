using System.Collections.Generic;
using System.IO;

namespace VCS_DOCs.Utilities
{
	public static class ConfigReader
	{
		public static List<string> GetSpecialities(string filePath)
		{
			var specialities = new List<string>();
			try
			{
				var lines = File.ReadAllLines(filePath);
				foreach (var line in lines)
				{
					if (line.StartsWith("Speciality"))
					{
						var parts = line.Split('=');
						if (parts.Length == 2)
						{
							specialities.Add(parts[1].Trim());
						}
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Ошибка чтения файла: {ex.Message}");
			}

			return specialities.Any() ? specialities : new List<string> { "Нет доступных специальностей" };
		}
	}
}