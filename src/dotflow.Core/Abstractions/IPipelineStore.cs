using Dotflow.Models;

namespace Dotflow.Abstractions;

public interface IPipelineStore
{
    System.Threading.Tasks.Task SaveRunAsync(WorkflowRun run, CancellationToken ct = default);
    System.Threading.Tasks.Task UpdateRunAsync(WorkflowRun run, CancellationToken ct = default);
    System.Threading.Tasks.Task AppendEventAsync(string runId, EventEnvelope evt, CancellationToken ct = default);
    System.Threading.Tasks.Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken ct = default);
    System.Threading.Tasks.Task<PagedResult<WorkflowRun>> ListRunsAsync(RunQuery query, CancellationToken ct = default);
    System.Threading.Tasks.Task<IReadOnlyList<EventEnvelope>> GetRunEventsAsync(string runId, CancellationToken ct = default);
    System.Threading.Tasks.Task<RunStats> GetStatsAsync(CancellationToken ct = default);
}
