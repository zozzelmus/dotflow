using Dotflow.Abstractions;

namespace Dotflow.Builder;

public class ConcurrentGroupBuilder
{
    private readonly List<Type> _taskTypes = new();

    public ConcurrentGroupBuilder AddTask<T>() where T : DotflowTask
    {
        _taskTypes.Add(typeof(T));
        return this;
    }

    internal TaskSlot.ConcurrentGroup Build()
        => new(_taskTypes.AsReadOnly());
}
