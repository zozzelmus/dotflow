using System.Collections.Concurrent;
using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Models;
using Microsoft.Extensions.Logging;

namespace Dotflow.Engine;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IReadOnlyList<WorkflowDefinition> _workflows;
    private readonly IPipelineStore _store;
    private readonly IEventBus _eventBus;
    private readonly PhaseExecutor _phaseExecutor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WorkflowEngine> _logger;

    // Per-run tracking: active phase count + completion source + cancellation
    private readonly Dictionary<string, RunTracker> _runs = [];
    private readonly Lock _runsLock = new();

    public WorkflowEngine(
        IReadOnlyList<WorkflowDefinition> workflows,
        IPipelineStore store,
        IEventBus eventBus,
        PhaseExecutor phaseExecutor,
        ILoggerFactory loggerFactory)
    {
        _workflows = workflows;
        _store = store;
        _eventBus = eventBus;
        _phaseExecutor = phaseExecutor;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<WorkflowEngine>();

        SubscribeToEvents();
    }

    public async Task<WorkflowRun> TriggerAsync(
        string workflowId,
        IReadOnlyDictionary<string, object?>? input = null,
        CancellationToken ct = default)
    {
        var definition = _workflows.FirstOrDefault(w => w.Id == workflowId)
            ?? throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var run = new WorkflowRun
        {
            WorkflowId = workflowId,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Input = input?.ToDictionary(k => k.Key, k => k.Value) ?? []
        };

        await _store.SaveRunAsync(run, ct);
        _logger.LogInformation("Workflow run {RunId} started for '{WorkflowId}'", run.Id, workflowId);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tracker = new RunTracker(cts);

        lock (_runsLock)
            _runs[run.Id] = tracker;

        _ = ExecuteWorkflowAsync(definition, run, tracker);

        return run;
    }

    public async Task CancelAsync(string runId, CancellationToken ct = default)
    {
        RunTracker? tracker;
        lock (_runsLock)
            _runs.TryGetValue(runId, out tracker);

        if (tracker is null)
        {
            _logger.LogWarning("Cannot cancel run {RunId}: not found or already completed", runId);
            return;
        }

        await tracker.Cts.CancelAsync();
        _logger.LogInformation("Cancellation requested for run {RunId}", runId);
    }

    private async Task ExecuteWorkflowAsync(WorkflowDefinition definition, WorkflowRun run, RunTracker tracker)
    {
        var ct = tracker.Cts.Token;
        try
        {
            var immediatePhases = definition.Phases
                .Where(p => p.Trigger is PhaseTrigger.Immediate)
                .ToList();

            // Start all immediate phases, incrementing the counter before each
            var immediateTasks = immediatePhases.Select(phase =>
            {
                tracker.IncrementActive();
                return StartPhaseAsync(run, phase, tracker, ct);
            }).ToList();

            // Signal that we've submitted all immediate phases;
            // the tracker can now resolve when the counter reaches zero
            tracker.SetImmediatesPending();

            await Task.WhenAll(immediateTasks);

            // Wait until all phases (including event-triggered ones) complete
            await tracker.CompletionTask;

            run.Status = run.Phases.Any(p => p.Status == RunStatus.Failed)
                ? RunStatus.Failed
                : RunStatus.Succeeded;
        }
        catch (OperationCanceledException) when (tracker.Cts.IsCancellationRequested)
        {
            run.Status = RunStatus.Cancelled;
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;
            _logger.LogError(ex, "Workflow run {RunId} failed unexpectedly", run.Id);
        }
        finally
        {
            run.FinishedAt = DateTimeOffset.UtcNow;
            await _store.UpdateRunAsync(run, CancellationToken.None);

            lock (_runsLock)
                _runs.Remove(run.Id);

            tracker.Cts.Dispose();
            _logger.LogInformation("Workflow run {RunId} completed with status {Status}", run.Id, run.Status);
        }
    }

    private async Task StartPhaseAsync(WorkflowRun run, PhaseDefinition phase, RunTracker tracker, CancellationToken ct)
    {
        try
        {
            var phaseRun = await _phaseExecutor.ExecuteAsync(
                phase, run.Id, run.Input, tracker.Context, _loggerFactory, ct);

            lock (run.Phases)
                run.Phases.Add(phaseRun);

            await _store.UpdateRunAsync(run, CancellationToken.None);
        }
        finally
        {
            tracker.DecrementActive();
        }
    }

    private void SubscribeToEvents()
    {
        _eventBus.Subscribe<DotflowEvent>(async (@event, ct) =>
        {
            if (@event.WorkflowRunId is null) return;

            RunTracker? tracker;
            lock (_runsLock)
                _runs.TryGetValue(@event.WorkflowRunId, out tracker);

            if (tracker is null) return;

            var run = await _store.GetRunAsync(@event.WorkflowRunId, ct);
            if (run is null || run.Status != RunStatus.Running) return;

            foreach (var workflow in _workflows)
            {
                if (workflow.Id != run.WorkflowId) continue;

                var matchingPhases = workflow.Phases
                    .Where(p => IsEventMatch(p.Trigger, @event))
                    .ToList();

                foreach (var phase in matchingPhases)
                {
                    tracker.IncrementActive();
                    _ = StartPhaseAsync(run, phase, tracker, tracker.Cts.Token);
                }
            }
        });
    }

    private static bool IsEventMatch(PhaseTrigger? trigger, DotflowEvent @event)
    {
        if (trigger is PhaseTrigger.OnEvent oe)
            return oe.EventType == (@event.GetType().FullName ?? @event.GetType().Name);

        if (trigger is null) return false;
        var triggerType = trigger.GetType();
        if (!triggerType.IsGenericType) return false;
        if (triggerType.GetGenericTypeDefinition() != typeof(OnEvent<>)) return false;
        var eventArg = triggerType.GetGenericArguments()[0];
        return eventArg.IsAssignableFrom(@event.GetType());
    }

    private sealed class RunTracker
    {
        public CancellationTokenSource Cts { get; }

        // Accumulated task outputs across all phases. Tasks see a merged view of
        // the original workflow input plus everything written here by prior tasks.
        public ConcurrentDictionary<string, object?> Context { get; } = new();

        private int _activePhases;
        private bool _immediatesPending;
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task CompletionTask => _tcs.Task;

        public RunTracker(CancellationTokenSource cts)
        {
            Cts = cts;
        }

        public void IncrementActive() => Interlocked.Increment(ref _activePhases);

        public void SetImmediatesPending()
        {
            _immediatesPending = true;
            TryComplete();
        }

        public void DecrementActive()
        {
            Interlocked.Decrement(ref _activePhases);
            TryComplete();
        }

        private void TryComplete()
        {
            if (_immediatesPending && Volatile.Read(ref _activePhases) == 0)
                _tcs.TrySetResult();
        }
    }
}
