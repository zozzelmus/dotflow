# dotflow

A .NET 10 workflow engine for building event-driven pipelines. Workflows are composed of phases wired together by events — no explicit dependency declarations, no DAG configuration. Phases start when their trigger fires.

## Core concepts

```
Workflow
  └── Phase[]         triggered immediately or on a named event
        └── TaskSlot  a single task, or a concurrent group (Task.WhenAll)
              └── DotflowTask   your code
```

Immediate phases all start concurrently at trigger time. Event-triggered phases start when a matching event is published from any task during the run. The event graph is the dependency graph.

## Getting started

Define tasks by subclassing `DotflowTask`:

```csharp
public class ValidateOrderTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct)
    {
        var orderId = context.Input["orderId"]?.ToString();
        // do work...
        context.SetOutput("customerId", "CUST-99");
        await context.PublishEventAsync(new OrderValidatedEvent { OrderId = orderId }, ct);
    }
}
```

Register workflows in DI:

```csharp
services.UseInMemoryStore(); // or UsePostgreSQLStore(connectionString)

services.AddTransient<ValidateOrderTask>();
services.AddTransient<ChargeCardTask>();
services.AddTransient<SendReceiptTask>();
services.AddTransient<ShipOrderTask>();

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
```

Trigger a run:

```csharp
var engine = services.GetRequiredService<IWorkflowEngine>();

var run = await engine.TriggerAsync("order-processing", new Dictionary<string, object?>
{
    ["orderId"] = "ORD-12345"
});
```

## Dashboard

Mount the built-in dashboard in any ASP.NET Core app:

```csharp
app.UseDotflowDashboard("/dotflow");
```

The dashboard provides a real-time view of workflows, run history, phase/task breakdowns, and the ability to trigger or cancel runs. It uses HTMX polling — no WebSocket or SSE required.

## Persistence

| Package | Use case |
|---|---|
| `dotflow.Persistence.InMemory` | Development and testing. Single-instance only. |
| `dotflow.Persistence.PostgreSQL` | Production. Dapper + Npgsql, run data stored as JSONB. |

Apply the PostgreSQL schema before first use:

```csharp
await Migrator.MigrateAsync(connectionString);
```

## Resiliency

Polly v8. Configure globally, per workflow, or per phase — most specific wins:

```csharp
.ConfigureResiliency(r => r
    .WithRetry(3, RetryStrategy.ExponentialBackoff)
    .WithTimeout(TimeSpan.FromSeconds(30))
    .WithCircuitBreaker(5, TimeSpan.FromSeconds(60)))
```

## Extensions

- **`dotflow.Extensions.MediatR`** — replaces the internal event bus with MediatR. Use `INotificationHandler<T>` for subscribers.
- **`dotflow.Extensions.Yaml`** — load workflow definitions from YAML configuration files.

## Running the samples

```bash
# Console demo
dotnet run --project samples/dotflow.Sample.Basic

# Web host with dashboard at http://localhost:5000/dotflow
dotnet run --project samples/dotflow.Sample.WebApi
```

## Requirements

- .NET 10
- PostgreSQL 14+ (if using the PostgreSQL persistence provider)
