namespace VCS_DOCs.TaskEngine
{
    public sealed class ModuleDescriptor
    {
        public ITaskModule Instance { get; init; } = default!;
        public DateTimeOffset NextRunUtc { get; set; } = DateTimeOffset.MinValue;
    }
}