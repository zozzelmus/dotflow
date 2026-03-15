using System.Collections.Concurrent;
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

public class ExecutionOrderTests
{
    // --- Task A/B/C for sequential order test ---

    private sealed class OrderTaskA : DotflowTask
    {
        public static ConcurrentQueue<string> ExecutionOrder = new();

        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            ExecutionOrder.Enqueue("A");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderTaskB : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            OrderTaskA.ExecutionOrder.Enqueue("B");
            return Task.CompletedTask;
        }
    }

    private sealed class OrderTaskC : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            OrderTaskA.ExecutionOrder.Enqueue("C");
            return Task.CompletedTask;
        }
    }

    // --- Tasks for concurrent parallelism test ---

    private sealed class ConcurrentTaskOne : DotflowTask
    {
        public static int ActiveCount;
        public static int PeakCount;

        public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref ActiveCount);
            InterlockedMax(ref PeakCount, current);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref ActiveCount);
        }
    }

    private sealed class ConcurrentTaskTwo : DotflowTask
    {
        public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            var current = Interlocked.Increment(ref ConcurrentTaskOne.ActiveCount);
            InterlockedMax(ref ConcurrentTaskOne.PeakCount, current);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref ConcurrentTaskOne.ActiveCount);
        }
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = location;
            if (current >= value) return;
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }

    // --- Tasks for "next slot waits for group" test ---

    private sealed class SlowGroupTask : DotflowTask
    {
        public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            await Task.Delay(80, ct);
            context.SetOutput("slowDone", "yes");
        }
    }

    private sealed class FastGroupTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            context.SetOutput("fastDone", "yes");
            return Task.CompletedTask;
        }
    }

    private sealed class CheckerTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            var slow = context.GetInput<string>("slowDone") ?? "<missing>";
            var fast = context.GetInput<string>("fastDone") ?? "<missing>";
            context.SetOutput("sawSlow", slow);
            context.SetOutput("sawFast", fast);
            return Task.CompletedTask;
        }
    }

    // --- Tasks for failure-stops-execution test ---

    private sealed class FailingSequentialTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("Deliberate failure");
    }

    private sealed class ShouldNotRunTask : DotflowTask
    {
        public static int ExecutionCount;

        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ExecutionCount);
            return Task.CompletedTask;
        }
    }

    // --- Engine builder ---

    private (WorkflowEngine engine, InMemoryPipelineStore store) BuildEngine(IReadOnlyList<WorkflowDefinition> workflows)
    {
        var services = new ServiceCollection();
        services.AddTransient<OrderTaskA>();
        services.AddTransient<OrderTaskB>();
        services.AddTransient<OrderTaskC>();
        services.AddTransient<ConcurrentTaskOne>();
        services.AddTransient<ConcurrentTaskTwo>();
        services.AddTransient<SlowGroupTask>();
        services.AddTransient<FastGroupTask>();
        services.AddTransient<CheckerTask>();
        services.AddTransient<FailingSequentialTask>();
        services.AddTransient<ShouldNotRunTask>();
        var sp = services.BuildServiceProvider();

        var store = new InMemoryPipelineStore();
        var eventBus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var taskExecutor = new TaskExecutor(sp, NullLogger<TaskExecutor>.Instance, null);
        var phaseExecutor = new PhaseExecutor(taskExecutor, eventBus, NullLogger<PhaseExecutor>.Instance);
        var engine = new WorkflowEngine(workflows, store, eventBus, phaseExecutor, NullLoggerFactory.Instance);
        return (engine, store);
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

    // --- Tests ---

    [Fact]
    public async Task SequentialTasks_ExecuteInDeclaredOrder()
    {
        OrderTaskA.ExecutionOrder = new ConcurrentQueue<string>();

        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf-order",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks =
                        [
                            new TaskSlot.SingleTask(typeof(OrderTaskA)),
                            new TaskSlot.SingleTask(typeof(OrderTaskB)),
                            new TaskSlot.SingleTask(typeof(OrderTaskC))
                        ]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf-order");
        await WaitForCompletionAsync(store, run.Id);

        Assert.Equal(["A", "B", "C"], OrderTaskA.ExecutionOrder.ToArray());
    }

    [Fact]
    public async Task ConcurrentGroup_TasksRunInParallel()
    {
        ConcurrentTaskOne.ActiveCount = 0;
        ConcurrentTaskOne.PeakCount = 0;

        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf-parallel",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks =
                        [
                            new TaskSlot.ConcurrentGroup(
                            [
                                typeof(ConcurrentTaskOne),
                                typeof(ConcurrentTaskTwo)
                            ])
                        ]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf-parallel");
        await WaitForCompletionAsync(store, run.Id);

        Assert.Equal(2, ConcurrentTaskOne.PeakCount);
    }

    [Fact]
    public async Task ConcurrentGroup_NextSlotStartsAfterAllGroupTasksComplete()
    {
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf-group-barrier",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        Tasks =
                        [
                            new TaskSlot.ConcurrentGroup(
                            [
                                typeof(SlowGroupTask),
                                typeof(FastGroupTask)
                            ]),
                            new TaskSlot.SingleTask(typeof(CheckerTask))
                        ]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf-group-barrier");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, completed!.Status);

        var checker = completed.Phases[0].Tasks.Single(t => t.TaskName == nameof(CheckerTask));
        Assert.Equal("yes", checker.Output["sawSlow"]);
        Assert.Equal("yes", checker.Output["sawFast"]);
    }

    [Fact]
    public async Task SequentialTasks_FailureStopsExecution_WhenContinueOnFailureIsFalse()
    {
        ShouldNotRunTask.ExecutionCount = 0;

        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf-fail-stop",
                Phases =
                [
                    new PhaseDefinition
                    {
                        Name = "p1",
                        Trigger = new PhaseTrigger.Immediate(),
                        ContinueOnFailure = false,
                        Tasks =
                        [
                            new TaskSlot.SingleTask(typeof(FailingSequentialTask)),
                            new TaskSlot.SingleTask(typeof(ShouldNotRunTask))
                        ]
                    }
                ]
            }
        };

        var (engine, store) = BuildEngine(workflows);
        var run = await engine.TriggerAsync("wf-fail-stop");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, completed!.Status);
        Assert.Equal(0, ShouldNotRunTask.ExecutionCount);
    }
}
