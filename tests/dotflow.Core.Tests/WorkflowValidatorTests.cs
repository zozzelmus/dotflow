using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Validation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dotflow.Core.Tests;

public class WorkflowValidatorTests
{
    private sealed class NoOpTask : DotflowTask
    {
        public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static WorkflowValidator BuildValidator(
        IReadOnlyList<WorkflowDefinition> workflows,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        var sp = services.BuildServiceProvider();
        return new WorkflowValidator(workflows, sp);
    }

    [Fact]
    public void Validate_DuplicateWorkflowId_Throws()
    {
        var workflows = new[]
        {
            new WorkflowDefinition { Id = "wf1" },
            new WorkflowDefinition { Id = "wf1" }
        };
        var validator = BuildValidator(workflows);

        var ex = Assert.Throws<DotflowValidationException>(() => validator.Validate());
        Assert.Contains(ex.Errors, e => e.Contains("Duplicate workflow ID"));
    }

    [Fact]
    public void Validate_PhaseWithNoTrigger_Throws()
    {
        var workflows = new[]
        {
            new WorkflowDefinition
            {
                Id = "wf1",
                Phases = [new PhaseDefinition { Name = "p1", Trigger = null }]
            }
        };
        var validator = BuildValidator(workflows, s => s.AddTransient<NoOpTask>());

        var ex = Assert.Throws<DotflowValidationException>(() => validator.Validate());
        Assert.Contains(ex.Errors, e => e.Contains("no trigger"));
    }

    [Fact]
    public void Validate_PhaseWithNoTasks_Throws()
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
                        Trigger = new PhaseTrigger.Immediate()
                    }
                ]
            }
        };
        var validator = BuildValidator(workflows);

        var ex = Assert.Throws<DotflowValidationException>(() => validator.Validate());
        Assert.Contains(ex.Errors, e => e.Contains("no tasks"));
    }

    [Fact]
    public void Validate_UnregisteredTask_Throws()
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
                        Tasks = [new TaskSlot.SingleTask(typeof(NoOpTask))]
                    }
                ]
            }
        };

        // Do NOT register NoOpTask in DI
        var validator = BuildValidator(workflows);

        var ex = Assert.Throws<DotflowValidationException>(() => validator.Validate());
        Assert.Contains(ex.Errors, e => e.Contains("not registered in DI"));
    }

    [Fact]
    public void Validate_ValidWorkflow_DoesNotThrow()
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
                        Tasks = [new TaskSlot.SingleTask(typeof(NoOpTask))]
                    }
                ]
            }
        };

        var validator = BuildValidator(workflows, s => s.AddTransient<NoOpTask>());

        // Should not throw
        validator.Validate();
    }
}
