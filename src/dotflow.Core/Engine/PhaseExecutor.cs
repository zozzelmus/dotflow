using System.Collections.Concurrent;
using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Models;
using Microsoft.Extensions.Logging;

namespace Dotflow.Engine;

internal sealed class PhaseExecutor
{
    private readonly TaskExecutor _taskExecutor;
    private readonly IEventBus _eventBus;
    private readonly ILogger<PhaseExecutor> _logger;

    public PhaseExecutor(TaskExecutor taskExecutor, IEventBus eventBus, ILogger<PhaseExecutor> logger)
    {
        _taskExecutor = taskExecutor;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<PhaseRun> ExecuteAsync(
        PhaseDefinition phase,
        string workflowRunId,
        IReadOnlyDictionary<string, object?> originalInput,
        ConcurrentDictionary<string, object?> sharedContext,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var phaseRun = new PhaseRun
        {
            PhaseName = phase.Name,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        _logger.LogInformation("Starting phase {PhaseName}", phase.Name);

        using var phaseScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["WorkflowRunId"] = workflowRunId,
            ["PhaseRunId"]    = phaseRun.Id,
            ["PhaseName"]     = phase.Name
        });

        try
        {
            foreach (var slot in phase.Tasks)
            {
                switch (slot)
                {
                    case TaskSlot.SingleTask single:
                        // Fresh snapshot per sequential task — picks up outputs from prior tasks in this phase.
                        var seqInput = Merge(originalInput, sharedContext);
                        await ExecuteSlotAsync(single.TaskType, phaseRun, workflowRunId, seqInput, sharedContext, phase, loggerFactory, ct);
                        if (phaseRun.Tasks.LastOrDefault()?.Status == RunStatus.Failed && !phase.ContinueOnFailure)
                        {
                            phaseRun.Status = RunStatus.Failed;
                            return phaseRun;
                        }
                        break;

                    case TaskSlot.ConcurrentGroup group:
                        // Single snapshot before the group starts — all tasks in the group see the same input.
                        var groupInput = Merge(originalInput, sharedContext);

                        if (phase.FailFastOnGroupFailure)
                        {
                            using var groupCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            var groupTasks = group.TaskTypes.Select(taskType => ExecuteSlotAsync(
                                taskType, phaseRun, workflowRunId, groupInput, sharedContext, phase, loggerFactory, groupCts.Token)
                                .ContinueWith(t =>
                                {
                                    if (t.IsCompletedSuccessfully &&
                                        phaseRun.Tasks.LastOrDefault(r => r.TaskName == taskType.Name)?.Status == RunStatus.Failed)
                                        groupCts.Cancel();
                                }, TaskScheduler.Default));
                            await Task.WhenAll(groupTasks);
                        }
                        else
                        {
                            var concurrentTasks = group.TaskTypes.Select(taskType =>
                                ExecuteSlotAsync(taskType, phaseRun, workflowRunId, groupInput, sharedContext, phase, loggerFactory, ct));
                            await Task.WhenAll(concurrentTasks);
                        }

                        if (phaseRun.Tasks.Any(t => t.Status == RunStatus.Failed) && !phase.ContinueOnFailure)
                        {
                            phaseRun.Status = RunStatus.Failed;
                            return phaseRun;
                        }
                        break;
                }
            }

            phaseRun.Status = phaseRun.Tasks.Any(t => t.Status == RunStatus.Failed)
                ? RunStatus.Failed
                : RunStatus.Succeeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            phaseRun.Status = RunStatus.Cancelled;
        }
        catch (Exception ex)
        {
            phaseRun.Status = RunStatus.Failed;
            _logger.LogError(ex, "Phase {PhaseName} failed unexpectedly", phase.Name);
        }
        finally
        {
            phaseRun.FinishedAt = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation("Phase {PhaseName} finished with status {Status}", phase.Name, phaseRun.Status);
        return phaseRun;
    }

    private async Task ExecuteSlotAsync(
        Type taskType,
        PhaseRun phaseRun,
        string workflowRunId,
        IReadOnlyDictionary<string, object?> taskInput,
        ConcurrentDictionary<string, object?> sharedContext,
        PhaseDefinition phase,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var taskLogger = loggerFactory.CreateLogger(taskType);
        var taskRun = new TaskRun { TaskName = taskType.Name };

        lock (phaseRun.Tasks)
            phaseRun.Tasks.Add(taskRun);

        using var taskScope = taskLogger.BeginScope(new Dictionary<string, object?>
        {
            ["WorkflowRunId"] = workflowRunId,
            ["PhaseRunId"]    = phaseRun.Id,
            ["TaskRunId"]     = taskRun.Id,
            ["TaskName"]      = taskType.Name
        });

        var context = new TaskContext(
            workflowRunId,
            phaseRun.Id,
            taskRun.Id,
            taskInput,
            sharedContext,
            _eventBus,
            taskLogger,
            ct);

        // TaskContext.SetOutput writes to sharedContext immediately, so outputs are
        // visible to any event handler that fires before ExecuteAsync returns.
        var result = await _taskExecutor.ExecuteAsync(taskType, context, phase.Resiliency, ct);

        lock (phaseRun.Tasks)
        {
            var idx = phaseRun.Tasks.IndexOf(taskRun);
            if (idx >= 0)
                phaseRun.Tasks[idx] = result;
        }
    }

    // Merges originalInput + accumulated context into a snapshot dict.
    // sharedContext values win on key conflict (later outputs override original input).
    private static IReadOnlyDictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?> originalInput,
        ConcurrentDictionary<string, object?> sharedContext)
    {
        var merged = new Dictionary<string, object?>(originalInput);
        foreach (var (key, value) in sharedContext)
            merged[key] = value;
        return merged;
    }
}
