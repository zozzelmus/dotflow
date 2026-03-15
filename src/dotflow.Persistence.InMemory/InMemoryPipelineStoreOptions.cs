namespace Dotflow.Persistence.InMemory;

public class InMemoryPipelineStoreOptions
{
    /// <summary>
    /// Maximum number of completed runs to retain in memory.
    /// Oldest completed runs are evicted when this limit is exceeded.
    /// Active (Running/Pending) runs are never evicted.
    /// Defaults to 1000. Set to 0 to disable the cap.
    /// </summary>
    public int MaxRunCount { get; set; } = 1000;
}
