using Dotflow;
using Dotflow.Configuration;
using Dotflow.Dashboard;
using Dotflow.Persistence.InMemory;
using Dotflow.Sample.WebApi.Tasks;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Register persistence and tasks
builder.Services.UseInMemoryStore();
builder.Services.AddTransient<SampleTask>();
builder.Services.AddTransient<ValidateOrderTask>();
builder.Services.AddTransient<ProcessPaymentTask>();
builder.Services.AddTransient<UpdateInventoryTask>();
builder.Services.AddTransient<SendConfirmationTask>();

// Configure dotflow workflows
builder.Services.AddDotflow(dotflow => dotflow
    .ConfigureResiliency(r => r.WithRetry(2, RetryStrategy.ExponentialBackoff))
    .AddWorkflow("order-processing", wf => wf
        .WithName("Order Processing")
        .AddPhase("validate", phase => phase
            .TriggeredImmediately()
            .AddTask<ValidateOrderTask>())
        .AddPhase("fulfill", phase => phase
            .TriggeredImmediately()
            .AddConcurrentGroup(g => g
                .AddTask<ProcessPaymentTask>()
                .AddTask<UpdateInventoryTask>()))
        .AddPhase("notify", phase => phase
            .TriggeredImmediately()
            .AddTask<SendConfirmationTask>()))
    .AddWorkflow("sample-workflow", wf => wf
        .WithName("Sample Workflow")
        .AddPhase("run", phase => phase
            .TriggeredImmediately()
            .AddTask<SampleTask>())));

var app = builder.Build();

// Mount the dotflow dashboard
app.UseDotflowDashboard("/dotflow");

app.MapGet("/", () => Results.Redirect("/dotflow"));

app.MapPost("/trigger/{workflowId}", async (string workflowId, Dotflow.Abstractions.IWorkflowEngine engine) =>
{
    var run = await engine.TriggerAsync(workflowId, new Dictionary<string, object?>
    {
        ["triggeredAt"] = DateTimeOffset.UtcNow.ToString("o")
    });
    return Results.Ok(new { runId = run.Id, dashboard = $"/dotflow/runs/{run.Id}" });
});

app.Run();
