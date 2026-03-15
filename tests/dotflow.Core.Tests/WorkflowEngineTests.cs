using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Engine;
using Dotflow.Events;
using Dotflow.Models;
using Dotflow.Persistence.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dotflow.Core.Tests;

public class WorkflowEngineTests
{
    private sealed class CountingTask : DotflowTask
    {
        public static int ExecutionCount;

        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecutionCount);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("Deliberate failure");
    }

    private (WorkflowEngine engine, InMemoryPipelineStore store) BuildEngine(
        IReadOnlyList<WorkflowDefinition> workflows,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<CountingTask>();
        services.AddTransient<FailingTask>();
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();

        var store = new InMemoryPipelineStore();
        var loggerFactory = NullLoggerFactory.Instance;
        var eventBus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var taskExecutor = new TaskExecutor(sp, NullLogger<TaskExecutor>.Instance, null);
        var phaseExecutor = new PhaseExecutor(taskExecutor, eventBus, NullLogger<PhaseExecutor>.Instance);

        var engine = new WorkflowEngine(workflows, store, eventBus, phaseExecutor, loggerFactory);
        return (engine, store);
    }

    [Fact]
    public async Task TriggerAsync_ImmediatePhase_ExecutesTask()
    {
        CountingTask.ExecutionCount = 0;
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf1",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks = [new TaskSlot.SingleTask(typeof(CountingTask))]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf1");

        // Allow time for async execution
        await WaitForCompletionAsync(store, run.Id);

        Assert.Equal(1, CountingTask.ExecutionCount);
        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, completed!.Status);
    }

    [Fact]
    public async Task TriggerAsync_FailingTask_SetsFailedStatus()
    {
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf1",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks = [new TaskSlot.SingleTask(typeof(FailingTask))]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf1");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, completed!.Status);
    }

    [Fact]
    public async Task TriggerAsync_UnknownWorkflow_Throws()
    {
        var (engine, _) = BuildEngine([]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.TriggerAsync("nonexistent"));
    }

    private static async Task WaitForCompletionAsync(InMemoryPipelineStore store, string runId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var run = await store.GetRunAsync(runId);
            if (run?.Status is RunStatus.Succeeded or RunStatus.Failed or RunStatus.Cancelled)
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Run {runId} did not complete within {timeoutMs}ms");
    }
}
