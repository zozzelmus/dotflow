namespace Dotflow.Builder;

public abstract record TaskSlot
{
    public sealed record SingleTask(Type TaskType) : TaskSlot;
    public sealed record ConcurrentGroup(IReadOnlyList<Type> TaskTypes) : TaskSlot;
}
