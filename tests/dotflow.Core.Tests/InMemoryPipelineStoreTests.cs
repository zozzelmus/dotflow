using Dotflow.Models;
using Dotflow.Persistence.InMemory;
using Xunit;

namespace Dotflow.Core.Tests;

public class InMemoryPipelineStoreTests
{
    private readonly InMemoryPipelineStore _store = new();

    [Fact]
    public async Task SaveAndGetRun_RoundTrips()
    {
        var run = new WorkflowRun { WorkflowId = "wf1" };
        await _store.SaveRunAsync(run);

        var retrieved = await _store.GetRunAsync(run.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved.Id);
        Assert.Equal("wf1", retrieved.WorkflowId);
    }

    [Fact]
    public async Task GetRunAsync_NotFound_ReturnsNull()
    {
        var result = await _store.GetRunAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRun_UpdatesExisting()
    {
        var run = new WorkflowRun { WorkflowId = "wf1" };
        await _store.SaveRunAsync(run);

        run.Status = RunStatus.Succeeded;
        await _store.UpdateRunAsync(run);

        var retrieved = await _store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, retrieved!.Status);
    }

    [Fact]
    public async Task ListRunsAsync_FiltersByWorkflowId()
    {
        var run1 = new WorkflowRun { WorkflowId = "wf1" };
        var run2 = new WorkflowRun { WorkflowId = "wf2" };
        await _store.SaveRunAsync(run1);
        await _store.SaveRunAsync(run2);

        var result = await _store.ListRunsAsync(new RunQuery { WorkflowId = "wf1" });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(run1.Id, result.Items[0].Id);
    }

    [Fact]
    public async Task ListRunsAsync_FiltersByStatus()
    {
        var run1 = new WorkflowRun { WorkflowId = "wf1" };
        run1.Status = RunStatus.Succeeded;
        var run2 = new WorkflowRun { WorkflowId = "wf1" };
        run2.Status = RunStatus.Failed;

        await _store.SaveRunAsync(run1);
        await _store.SaveRunAsync(run2);

        var result = await _store.ListRunsAsync(new RunQuery { Status = RunStatus.Succeeded });

        Assert.All(result.Items, r => Assert.Equal(RunStatus.Succeeded, r.Status));
    }

    [Fact]
    public async Task AppendEventAsync_StoresEvent()
    {
        var run = new WorkflowRun { WorkflowId = "wf1" };
        await _store.SaveRunAsync(run);

        var evt = new EventEnvelope
        {
            RunId = run.Id,
            EventType = "TestEvent",
            Payload = "{}"
        };
        await _store.AppendEventAsync(run.Id, evt);

        var events = await _store.GetRunEventsAsync(run.Id);
        Assert.Single(events);
        Assert.Equal("TestEvent", events[0].EventType);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        var run1 = new WorkflowRun { WorkflowId = "wf1" };
        run1.Status = RunStatus.Succeeded;
        var run2 = new WorkflowRun { WorkflowId = "wf1" };
        run2.Status = RunStatus.Failed;

        await _store.SaveRunAsync(run1);
        await _store.SaveRunAsync(run2);

        var stats = await _store.GetStatsAsync();

        Assert.Equal(2, stats.TotalRuns);
        Assert.Equal(1, stats.SucceededRuns);
        Assert.Equal(1, stats.FailedRuns);
    }
}
