using System.Collections.Concurrent;
using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Engine;

internal sealed class TaskContext : ITaskContext
{
    public string WorkflowRunId { get; }
    public string PhaseRunId { get; }
    public string TaskRunId { get; }
    public IReadOnlyDictionary<string, object?> Input { get; }
    public IDictionary<string, object?> Output { get; } = new Dictionary<string, object?>();
    public IEventBus Events { get; }
    public ILogger Logger { get; }
    public CancellationToken CancellationToken { get; }

    private readonly ConcurrentDictionary<string, object?> _sharedContext;

    public TaskContext(
        string workflowRunId,
        string phaseRunId,
        string taskRunId,
        IReadOnlyDictionary<string, object?> input,
        ConcurrentDictionary<string, object?> sharedContext,
        IEventBus events,
        ILogger logger,
        CancellationToken ct)
    {
        WorkflowRunId = workflowRunId;
        PhaseRunId = phaseRunId;
        TaskRunId = taskRunId;
        Input = input;
        _sharedContext = sharedContext;
        Events = events;
        Logger = logger;
        CancellationToken = ct;
    }

    public T? GetInput<T>(string key)
    {
        if (Input.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return default;
    }

    public void SetOutput<T>(string key, T value)
    {
        Output[key] = value;
        // Write immediately to shared context so subsequent tasks (and event-triggered
        // phases starting within the same PublishAsync call) see this output right away.
        _sharedContext[key] = value;
    }
}
