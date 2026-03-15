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

### YAML workflows

Register workflows from a YAML file instead of the fluent builder:

```csharp
services.AddDotflow(dotflow => dotflow
    .LoadFromYaml(File.ReadAllText("workflows.yaml")));
```

Task and event types must be assembly-qualified names (`"MyApp.Tasks.ValidateOrderTask, MyApp"`).

**Basic** — a single phase, tasks run sequentially:

```yaml
workflows:
  - id: order-processing
    name: Order Processing
    phases:
      - name: validation
        tasks:
          - type: "MyApp.Tasks.ValidateOrderTask, MyApp"
          - type: "MyApp.Tasks.EnrichOrderTask, MyApp"
```

**Event-driven** — phases wired together by events. Each phase starts when the named event is published by a task in a preceding phase:

```yaml
workflows:
  - id: order-processing
    name: Order Processing
    phases:
      - name: validation
        tasks:
          - type: "MyApp.Tasks.ValidateOrderTask, MyApp"

      - name: payment
        trigger:
          type: OnEvent
          eventType: "MyApp.Events.OrderValidatedEvent, MyApp"
        tasks:
          - type: "MyApp.Tasks.ChargeCardTask, MyApp"
          - type: "MyApp.Tasks.SendReceiptTask, MyApp"

      - name: fulfillment
        trigger:
          type: OnEvent
          eventType: "MyApp.Events.PaymentProcessedEvent, MyApp"
        tasks:
          - type: "MyApp.Tasks.ShipOrderTask, MyApp"
```

**Parallelism** — multiple `Immediate` phases all start concurrently at trigger time:

```yaml
workflows:
  - id: new-user-setup
    name: New User Setup
    phases:
      - name: send-welcome-email
        tasks:
          - type: "MyApp.Tasks.SendWelcomeEmailTask, MyApp"

      - name: provision-storage
        tasks:
          - type: "MyApp.Tasks.ProvisionStorageTask, MyApp"

      - name: sync-to-crm
        tasks:
          - type: "MyApp.Tasks.SyncToCrmTask, MyApp"
```

Phases with no trigger (or `trigger: { type: Immediate }`) all fire at the same time. Each phase's task list runs sequentially within that phase. For in-code concurrent task groups within a single phase, use the fluent builder's `AddConcurrentGroup`.

## Running the samples

```bash
# Console demo (fluent builder)
dotnet run --project samples/dotflow.Sample.Basic

# YAML config demo (parallel phases + event-driven)
dotnet run --project samples/dotflow.Sample.Yaml

# Web host with dashboard at http://localhost:5000/dotflow
dotnet run --project samples/dotflow.Sample.WebApi
```

## Requirements

- .NET 10
- PostgreSQL 14+ (if using the PostgreSQL persistence provider)
