using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Engine;
using Dotflow.Events;
using Dotflow.Models;
using Dotflow.Persistence.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dotflow.Core.Tests;

public class OutputPropagationTests
{
    // Writes a value to Output and publishes an event to trigger the next phase.
    private sealed class ProducerTask : DotflowTask
    {
        public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            context.SetOutput("producedValue", "hello-from-producer");
            await context.Events.PublishAsync(new ProducerDoneEvent
            {
                WorkflowRunId = context.WorkflowRunId
            }, ct);
        }
    }

    // Reads the value produced by ProducerTask and writes what it found.
    private sealed class ConsumerTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            var value = context.GetInput<string>("producedValue");
            context.SetOutput("sawProducedValue", value ?? "<missing>");
            return Task.CompletedTask;
        }
    }

    // Within a single phase, task B should see task A's output.
    private sealed class PhaseTaskA : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            context.SetOutput("fromA", "written-by-A");
            return Task.CompletedTask;
        }
    }

    private sealed class PhaseTaskB : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            var value = context.GetInput<string>("fromA");
            context.SetOutput("sawFromA", value ?? "<missing>");
            return Task.CompletedTask;
        }
    }

    private record ProducerDoneEvent : DotflowEvent;

    private (WorkflowEngine engine, InMemoryPipelineStore store) BuildEngine(
        IReadOnlyList<WorkflowDefinition> workflows,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<ProducerTask>();
        services.AddTransient<ConsumerTask>();
        services.AddTransient<PhaseTaskA>();
        services.AddTransient<PhaseTaskB>();
        configure?.Invoke(services);
        var sp = services.BuildServiceProvider();

        var store = new InMemoryPipelineStore();
        var eventBus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var taskExecutor = new TaskExecutor(sp, NullLogger<TaskExecutor>.Instance, null);
        var phaseExecutor = new PhaseExecutor(taskExecutor, eventBus, NullLogger<PhaseExecutor>.Instance);
        var engine = new WorkflowEngine(workflows, store, eventBus, phaseExecutor, NullLoggerFactory.Instance);
        return (engine, store);
    }

    [Fact]
    public async Task PhaseOutputsAreVisibleInSubsequentPhase()
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
                        Name = "produce",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks = [new TaskSlot.SingleTask(typeof(ProducerTask))]
                    },
                    new PhaseDefinition
                    {
                        Name = "consume",
                        Trigger = new OnEvent<ProducerDoneEvent>(),
                        Tasks = [new TaskSlot.SingleTask(typeof(ConsumerTask))]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf1");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, completed!.Status);

        var consumePhase = completed.Phases.Single(p => p.PhaseName == "consume");
        var consumerTask = consumePhase.Tasks.Single();
        Assert.Equal("hello-from-producer", consumerTask.Output["sawProducedValue"]);
    }

    [Fact]
    public async Task SequentialTasksWithinPhaseShareOutputs()
    {
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf2",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "sequential",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks =
                        [
                            new TaskSlot.SingleTask(typeof(PhaseTaskA)),
                            new TaskSlot.SingleTask(typeof(PhaseTaskB))
                        ]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf2");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, completed!.Status);

        var taskB = completed.Phases[0].Tasks.Single(t => t.TaskName == nameof(PhaseTaskB));
        Assert.Equal("written-by-A", taskB.Output["sawFromA"]);
    }

    [Fact]
    public async Task OriginalInputIsVisibleInAllPhases()
    {
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf3",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "check",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks = [new TaskSlot.SingleTask(typeof(ConsumerTask))]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf3", new Dictionary<string, object?>
        {
            ["producedValue"] = "from-original-input"
        });
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        var task = completed!.Phases[0].Tasks[0];
        Assert.Equal("from-original-input", task.Output["sawProducedValue"]);
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
