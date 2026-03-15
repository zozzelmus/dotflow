using Dotflow.Abstractions;
using Dotflow.Configuration;
using Dotflow.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Dotflow.Engine;

internal sealed class TaskExecutor
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TaskExecutor> _logger;
    private readonly ResiliencyOptions? _globalResiliency;

    public TaskExecutor(IServiceProvider services, ILogger<TaskExecutor> logger, ResiliencyOptions? globalResiliency)
    {
        _services = services;
        _logger = logger;
        _globalResiliency = globalResiliency;
    }

    public async Task<TaskRun> ExecuteAsync(
        Type taskType,
        ITaskContext context,
        ResiliencyOptions? phaseResiliency,
        CancellationToken ct)
    {
        var taskRun = new TaskRun
        {
            TaskName = taskType.Name,
            StartedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Running
        };

        var effective = phaseResiliency ?? _globalResiliency;
        var pipeline = ResiliencyPolicyFactory.Build(effective);

        try
        {
            await pipeline.ExecuteAsync(async token =>
            {
                taskRun.AttemptCount++;
                _logger.LogDebug("Executing task {TaskName} (attempt {Attempt})", taskRun.TaskName, taskRun.AttemptCount);

                var task = (DotflowTask)_services.GetRequiredService(taskType);
                await task.ExecuteAsync(context, token);
            }, ct);

            foreach (var (key, value) in context.Output)
                taskRun.Output[key] = value;

            taskRun.Status = RunStatus.Succeeded;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            taskRun.Status = RunStatus.Cancelled;
            taskRun.ErrorMessage = "Cancelled";
        }
        catch (TimeoutRejectedException)
        {
            taskRun.Status = RunStatus.TimedOut;
            taskRun.ErrorMessage = "Task timed out";
            _logger.LogWarning("Task {TaskName} timed out", taskRun.TaskName);
        }
        catch (Exception ex)
        {
            taskRun.Status = RunStatus.Failed;
            taskRun.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Task {TaskName} failed after {Attempts} attempt(s)", taskRun.TaskName, taskRun.AttemptCount);
        }
        finally
        {
            taskRun.FinishedAt = DateTimeOffset.UtcNow;
        }

        return taskRun;
    }
}
