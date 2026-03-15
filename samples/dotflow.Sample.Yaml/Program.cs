using Dotflow;
using Dotflow.Abstractions;
using Dotflow.Extensions.Yaml;
using Dotflow.Persistence.InMemory;
using Dotflow.Sample.Yaml.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.UseInMemoryStore();

        services.AddTransient<SendWelcomeEmailTask>();
        services.AddTransient<ProvisionStorageTask>();
        services.AddTransient<ActivateAccountTask>();

        services.AddDotflow(dotflow => dotflow
            .LoadFromYaml(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "workflow.yaml"))));
    })
    .ConfigureLogging(logging => logging
        .SetMinimumLevel(LogLevel.Debug)
        .AddConsole())
    .Build();

await host.StartAsync();

var engine = host.Services.GetRequiredService<IWorkflowEngine>();
var store = host.Services.GetRequiredService<IPipelineStore>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=== Triggering user-onboarding workflow ===");

var run = await engine.TriggerAsync("user-onboarding", new Dictionary<string, object?>
{
    ["userId"] = "USR-42"
});

logger.LogInformation("Run {RunId} created", run.Id);

var deadline = DateTime.UtcNow.AddSeconds(30);
while (DateTime.UtcNow < deadline)
{
    await Task.Delay(200);
    var current = await store.GetRunAsync(run.Id);
    if (current?.Status is Dotflow.Models.RunStatus.Succeeded or Dotflow.Models.RunStatus.Failed)
    {
        logger.LogInformation("=== Workflow completed with status: {Status} ===", current.Status);
        foreach (var phase in current.Phases)
        {
            logger.LogInformation("  Phase '{Name}': {Status}", phase.PhaseName, phase.Status);
            foreach (var task in phase.Tasks)
                logger.LogInformation("    Task '{Name}': {Status}", task.TaskName, task.Status);
        }
        break;
    }
}

await host.StopAsync();
