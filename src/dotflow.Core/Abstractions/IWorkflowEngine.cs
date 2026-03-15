using Dotflow.Models;

namespace Dotflow.Abstractions;

public interface IWorkflowEngine
{
    System.Threading.Tasks.Task<WorkflowRun> TriggerAsync(
        string workflowId,
        IReadOnlyDictionary<string, object?>? input = null,
        CancellationToken ct = default);

    System.Threading.Tasks.Task CancelAsync(string runId, CancellationToken ct = default);
}
