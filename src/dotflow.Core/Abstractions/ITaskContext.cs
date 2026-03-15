using Microsoft.Extensions.Logging;

namespace Dotflow.Abstractions;

public interface ITaskContext
{
    string WorkflowRunId { get; }
    string PhaseRunId { get; }
    string TaskRunId { get; }
    IReadOnlyDictionary<string, object?> Input { get; }
    IDictionary<string, object?> Output { get; }
    IEventBus Events { get; }
    ILogger Logger { get; }
    CancellationToken CancellationToken { get; }

    T? GetInput<T>(string key);
    void SetOutput<T>(string key, T value);
}
