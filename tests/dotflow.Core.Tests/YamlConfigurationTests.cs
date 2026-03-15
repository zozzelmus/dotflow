using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Extensions.Yaml;
using Dotflow.Models;
using Dotflow.Persistence.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dotflow.Core.Tests;

// Top-level so they can be referenced by fully qualified name in YAML strings
public sealed class YamlSimpleTask : DotflowTask
{
    public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}

public sealed class YamlSecondTask : DotflowTask
{
    public override Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
        => Task.CompletedTask;
}

public sealed record YamlTestEvent : DotflowEvent;

public class YamlConfigurationTests
{
    private static readonly string TaskType = $"{typeof(YamlSimpleTask).FullName}, dotflow.Core.Tests";
    private static readonly string SecondTaskType = $"{typeof(YamlSecondTask).FullName}, dotflow.Core.Tests";
    private static readonly string EventType = $"{typeof(YamlTestEvent).FullName}, dotflow.Core.Tests";

    private static IWorkflowEngine BuildEngine(string yaml)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseInMemoryStore();
        services.AddDotflowTasksFromAssembly(typeof(YamlSimpleTask).Assembly);
        services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml));
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IWorkflowEngine>();
    }

    [Fact]
    public void LoadFromYaml_ImmediateTrigger_RegistersWorkflow()
    {
        var yaml = $"""
            workflows:
              - id: test-wf
                name: Test Workflow
                phases:
                  - name: phase1
                    trigger:
                      type: Immediate
                    tasks:
                      - type: "{TaskType}"
            """;

        // If this doesn't throw, the workflow was registered and validated successfully
        var engine = BuildEngine(yaml);
        Assert.NotNull(engine);
    }

    [Fact]
    public async Task LoadFromYaml_ImmediateTrigger_ExecutesSuccessfully()
    {
        var yaml = $"""
            workflows:
              - id: test-wf
                phases:
                  - name: phase1
                    trigger:
                      type: Immediate
                    tasks:
                      - type: "{TaskType}"
            """;

        var services = new ServiceCollection();
        services.AddLogging();
        services.UseInMemoryStore();
        services.AddDotflowTasksFromAssembly(typeof(YamlSimpleTask).Assembly);
        services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml));
        var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<IWorkflowEngine>();
        var store = sp.GetRequiredService<IPipelineStore>();

        var run = await engine.TriggerAsync("test-wf");
        await WaitForCompletionAsync(store, run.Id);

        var completed = await store.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Succeeded, completed!.Status);
    }

    [Fact]
    public void LoadFromYaml_OnEventTrigger_RegistersWorkflow()
    {
        var yaml = $"""
            workflows:
              - id: test-wf
                phases:
                  - name: phase1
                    trigger:
                      type: Immediate
                    tasks:
                      - type: "{TaskType}"
                  - name: phase2
                    trigger:
                      type: OnEvent
                      eventType: "{EventType}"
                    tasks:
                      - type: "{SecondTaskType}"
            """;

        var engine = BuildEngine(yaml);
        Assert.NotNull(engine);
    }

    [Fact]
    public void LoadFromYaml_UnresolvableTaskType_Throws()
    {
        var yaml = """
            workflows:
              - id: test-wf
                phases:
                  - name: phase1
                    trigger:
                      type: Immediate
                    tasks:
                      - type: "Some.Nonexistent.TaskType, SomeAssembly"
            """;

        var services = new ServiceCollection();
        services.UseInMemoryStore();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml)));
        Assert.Contains("could not be resolved", ex.Message);
    }

    [Fact]
    public void LoadFromYaml_UnresolvableEventType_Throws()
    {
        var yaml = $"""
            workflows:
              - id: test-wf
                phases:
                  - name: phase1
                    trigger:
                      type: OnEvent
                      eventType: "Some.Nonexistent.Event, SomeAssembly"
                    tasks:
                      - type: "{TaskType}"
            """;

        var services = new ServiceCollection();
        services.UseInMemoryStore();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml)));
        Assert.Contains("could not be resolved", ex.Message);
    }

    [Fact]
    public void LoadFromYaml_MissingWorkflowId_Throws()
    {
        var yaml = $"""
            workflows:
              - phases:
                  - name: phase1
                    trigger:
                      type: Immediate
                    tasks:
                      - type: "{TaskType}"
            """;

        var services = new ServiceCollection();
        services.UseInMemoryStore();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml)));
    }

    [Fact]
    public void LoadFromYaml_UnknownTriggerType_Throws()
    {
        var yaml = $"""
            workflows:
              - id: test-wf
                phases:
                  - name: phase1
                    trigger:
                      type: BadTrigger
                    tasks:
                      - type: "{TaskType}"
            """;

        var services = new ServiceCollection();
        services.UseInMemoryStore();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddDotflow(dotflow => dotflow.LoadFromYaml(yaml)));
        Assert.Contains("Unknown trigger type", ex.Message);
    }

    [Fact]
    public void AddDotflowTasksFromAssembly_RegistersAllTaskSubclasses()
    {
        var services = new ServiceCollection();
        services.AddDotflowTasksFromAssembly(typeof(YamlSimpleTask).Assembly);
        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<YamlSimpleTask>());
        Assert.NotNull(sp.GetService<YamlSecondTask>());
    }

    private static async Task WaitForCompletionAsync(IPipelineStore store, string runId, int timeoutMs = 5000)
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
