using System.Collections.Concurrent;
using Dotflow.Abstractions;
using Dotflow.Models;

namespace Dotflow.Persistence.InMemory;

/// <summary>
/// In-memory pipeline store for development and testing.
/// WARNING: Single-instance only. Not safe for multi-container deployments.
/// Use dotflow.Persistence.PostgreSQL for production.
/// </summary>
public sealed class InMemoryPipelineStore : IPipelineStore
{
    private readonly ConcurrentDictionary<string, WorkflowRun> _runs = new();
    private readonly ConcurrentDictionary<string, List<EventEnvelope>> _events = new();
    private readonly Lock _eventsLock = new();
    private readonly InMemoryPipelineStoreOptions _options;

    public InMemoryPipelineStore(InMemoryPipelineStoreOptions? options = null)
    {
        _options = options ?? new InMemoryPipelineStoreOptions();
    }

    public Task SaveRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        return Task.CompletedTask;
    }

    public Task UpdateRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        _runs[run.Id] = run;
        TrimIfNeeded();
        return Task.CompletedTask;
    }

    private void TrimIfNeeded()
    {
        if (_options.MaxRunCount <= 0 || _runs.Count <= _options.MaxRunCount) return;

        var evictable = _runs.Values
            .Where(r => r.Status != RunStatus.Running && r.Status != RunStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(_runs.Count - _options.MaxRunCount)
            .Select(r => r.Id)
            .ToList();

        foreach (var id in evictable)
        {
            _runs.TryRemove(id, out _);
            _events.TryRemove(id, out _);
        }
    }

    public Task AppendEventAsync(string runId, EventEnvelope evt, CancellationToken ct = default)
    {
        lock (_eventsLock)
        {
            if (!_events.TryGetValue(runId, out var list))
                _events[runId] = list = new();
            list.Add(evt);
        }
        return Task.CompletedTask;
    }

    public Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        _runs.TryGetValue(runId, out var run);
        return Task.FromResult(run);
    }

    public Task<PagedResult<WorkflowRun>> ListRunsAsync(RunQuery query, CancellationToken ct = default)
    {
        var filtered = _runs.Values.AsEnumerable();

        if (query.WorkflowId is not null)
            filtered = filtered.Where(r => r.WorkflowId == query.WorkflowId);
        if (query.Status.HasValue)
            filtered = filtered.Where(r => r.Status == query.Status.Value);
        if (query.From.HasValue)
            filtered = filtered.Where(r => r.CreatedAt >= query.From.Value);
        if (query.To.HasValue)
            filtered = filtered.Where(r => r.CreatedAt <= query.To.Value);

        var ordered = filtered.OrderByDescending(r => r.CreatedAt).ToList();
        var total = ordered.Count;
        var items = ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return Task.FromResult(new PagedResult<WorkflowRun>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    public Task<IReadOnlyList<EventEnvelope>> GetRunEventsAsync(string runId, CancellationToken ct = default)
    {
        lock (_eventsLock)
        {
            if (_events.TryGetValue(runId, out var list))
                return Task.FromResult<IReadOnlyList<EventEnvelope>>(list.AsReadOnly());
        }
        return Task.FromResult<IReadOnlyList<EventEnvelope>>([]);
    }

    public Task<RunStats> GetStatsAsync(CancellationToken ct = default)
    {
        var all = _runs.Values.ToList();
        var completed = all.Where(r => r.FinishedAt.HasValue).ToList();
        var avgDuration = completed.Count > 0
            ? TimeSpan.FromMilliseconds(completed
                .Average(r => (r.FinishedAt!.Value - r.StartedAt!.Value).TotalMilliseconds))
            : (TimeSpan?)null;

        return Task.FromResult(new RunStats
        {
            TotalRuns = all.Count,
            SucceededRuns = all.Count(r => r.Status == RunStatus.Succeeded),
            FailedRuns = all.Count(r => r.Status == RunStatus.Failed),
            RunningRuns = all.Count(r => r.Status == RunStatus.Running),
            AverageDuration = avgDuration
        });
    }
}
