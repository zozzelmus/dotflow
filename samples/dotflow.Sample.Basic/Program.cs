using Dotflow;
using Dotflow.Configuration;
using Dotflow.Abstractions;
using Dotflow.Persistence.InMemory;
using Dotflow.Sample.Basic;
using Dotflow.Sample.Basic.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Register the in-memory store (dev only)
        services.UseInMemoryStore();

        // Register task implementations
        services.AddTransient<ValidateOrderTask>();
        services.AddTransient<ChargeCardTask>();
        services.AddTransient<SendReceiptTask>();
        services.AddTransient<ShipOrderTask>();

        // Configure dotflow
        services.AddDotflow(dotflow => dotflow
            .ConfigureResiliency(r => r
                .WithRetry(3, RetryStrategy.ExponentialBackoff, TimeSpan.FromMilliseconds(200)))
            .AddWorkflow("order-processing", wf => wf
                .WithName("Order Processing")
                .AddPhase("validation", phase => phase
                    .TriggeredImmediately()
                    .AddTask<ValidateOrderTask>())
                .AddPhase("payment", phase => phase
                    .TriggeredOn<OrderValidatedEvent>()
                    .AddTask<ChargeCardTask>()
                    .AddTask<SendReceiptTask>())
                .AddPhase("fulfillment", phase => phase
                    .TriggeredOn<PaymentProcessedEvent>()
                    .AddTask<ShipOrderTask>())));
    })
    .ConfigureLogging(logging => logging
        .SetMinimumLevel(LogLevel.Debug)
        .AddConsole())
    .Build();

// Start background services (runs validation)
await host.StartAsync();

var engine = host.Services.GetRequiredService<IWorkflowEngine>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=== Triggering order-processing workflow ===");

var run = await engine.TriggerAsync("order-processing", new Dictionary<string, object?>
{
    ["orderId"] = "ORD-12345"
});

logger.LogInformation("Run {RunId} created", run.Id);

// Wait for the workflow to complete (event-driven, so we poll for demo purposes)
var store = host.Services.GetRequiredService<Dotflow.Abstractions.IPipelineStore>();
var timeout = TimeSpan.FromSeconds(30);
var deadline = DateTime.UtcNow + timeout;

while (DateTime.UtcNow < deadline)
{
    await Task.Delay(500);
    var current = await store.GetRunAsync(run.Id);
    if (current?.Status is Dotflow.Models.RunStatus.Succeeded or Dotflow.Models.RunStatus.Failed)
    {
        logger.LogInformation("=== Workflow completed with status: {Status} ===", current.Status);
        foreach (var phase in current.Phases)
        {
            logger.LogInformation("  Phase '{Name}': {Status} ({Tasks} task(s))",
                phase.PhaseName, phase.Status, phase.Tasks.Count);
            foreach (var task in phase.Tasks)
                logger.LogInformation("    Task '{Name}': {Status} (attempts: {Attempts})",
                    task.TaskName, task.Status, task.AttemptCount);
        }
        break;
    }
}

await host.StopAsync();
