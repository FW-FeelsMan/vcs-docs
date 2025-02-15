using System;
using System.Diagnostics;
using System.IO;

namespace VCS_DOCs.Utilities
{
	public class StpToIfcConverter
	{
		public string ConvertStpToIfcUsingFreeCAD(string stpFilePath, string outputIfcPath)
		{
			if (string.IsNullOrEmpty(stpFilePath) || !File.Exists(stpFilePath))
				throw new ArgumentException("Invalid STEP file path.");
			if (string.IsNullOrEmpty(outputIfcPath))
				throw new ArgumentException("Invalid output IFC file path.");

			string script = $@"
				import FreeCAD
				import Part
				import ImportGui
				doc = FreeCAD.newDocument()
				ImportGui.insert(r'{stpFilePath}', doc.Name)
				doc.recompute()
				ImportGui.export(doc.Objects, r'{outputIfcPath}')
				FreeCAD.closeDocument(doc.Name)
				";
			string tempScriptPath = Path.Combine(Path.GetTempPath(), "convert.py");
			File.WriteAllText(tempScriptPath, script);
			ProcessStartInfo startInfo = new ProcessStartInfo
			{
				FileName = "FreeCADCmd.exe",
				Arguments = $"-c \"exec(compile(open(r'{tempScriptPath}').read(), r'{tempScriptPath}', 'exec'))\"",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using (Process process = Process.Start(startInfo))
			{
				string output = process.StandardOutput.ReadToEnd();
				string error = process.StandardError.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					throw new Exception("Error converting file: " + error);
				}
			}
			File.Delete(tempScriptPath);
			return outputIfcPath;
		}
	}
}
