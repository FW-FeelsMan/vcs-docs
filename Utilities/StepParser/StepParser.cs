using System.Text.RegularExpressions;

namespace VCS_DOCs.Utilities.StepParser
{
	public class StepParser
	{
		public List<StepEntity> Parse(string filePath)
		{
			var entities = new List<StepEntity>();
			var lines = File.ReadAllLines(filePath);

			foreach (var line in lines)
			{
				if (line.StartsWith("#"))
				{
					var match = Regex.Match(line, @"#(\d+)\s*=\s*(\w+)\s*\(([^)]*)\);");
					if (match.Success)
					{
						var entity = new StepEntity
						{
							Id = int.Parse(match.Groups[1].Value),
							Type = match.Groups[2].Value,
							Parameters = match.Groups[3].Value.Split(',')
						};
						entities.Add(entity);
					}
				}
			}

			return entities;
		}
	}
	public class StepEntity
	{
		public int Id { get; set; }
		public string Type { get; set; }
		public string[] Parameters { get; set; }
	}
}
