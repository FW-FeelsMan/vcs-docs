using System.Reflection;

namespace VCS_DOCs.TaskEngine
{
	public class TaskRunner
	{
		private readonly string _modulesPath;
		private readonly Dictionary<string, ITaskModule> _loadedModules = new();

		public TaskRunner(string modulesPath)
		{
			_modulesPath = Path.GetFullPath(modulesPath);
			LoadModules();
		}

		private void LoadModules()
		{
			if (!Directory.Exists(_modulesPath))
				return;

			foreach (var dll in Directory.GetFiles(_modulesPath, "*.dll"))
			{
				try
				{
					var asm = Assembly.LoadFrom(dll);
					var moduleTypes = asm.GetTypes()
						.Where(t => typeof(ITaskModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

					foreach (var type in moduleTypes)
					{
						if (Activator.CreateInstance(type) is ITaskModule module)
						{
							_loadedModules[module.Name] = module;
						}
					}
				}
				catch (Exception ex)
				{
					File.WriteAllText(Path.Combine(_modulesPath, "load_error.txt"), $"[{dll}]: {ex.Message}\n{ex.StackTrace}");
				}
			}
		}

		public async Task<TaskResult> RunTaskAsync(string moduleName, string inputJson)
		{
			if (!_loadedModules.TryGetValue(moduleName, out var module))
				throw new InvalidOperationException($"Модуль '{moduleName}' не найден.");

			var context = TaskContext.FromJson(inputJson);
			return await module.ExecuteAsync(context);
		}
	}
}