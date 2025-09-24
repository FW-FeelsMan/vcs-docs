namespace VCS_DOCs.TaskEngine
{
    public sealed class TaskHostOptions
    {
        public string ModulesPath { get; set; } = "Modules";
        public int ScanPeriodSeconds { get; set; } = 5;      // период тика планировщика
        public int MaxConcurrency { get; set; } = 2;         // ограничение параллельных задач
        public bool ThrowOnModuleError { get; set; } = false; // падать ли при ошибке модуля
    }
}