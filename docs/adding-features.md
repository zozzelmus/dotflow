# Adding Features

Common extension points and how to implement them.

---

## Adding a new DotflowTask

1. Create a class in your application (or a new library) that extends `DotflowTask`:

```csharp
public class MyTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        var value = context.GetInput<string>("myKey");
        // ... do work ...
        context.SetOutput("result", "done");

        // Optionally publish an event to trigger the next phase
        await context.Events.PublishAsync(new MyTaskCompletedEvent
        {
            WorkflowRunId = context.WorkflowRunId,
            // ...
        }, ct);
    }
}
```

2. Register the task in DI:

```csharp
services.AddTransient<MyTask>();
```

3. Reference it in a phase definition:

```csharp
.AddPhase("my-phase", phase => phase
    .TriggeredImmediately()
    .AddTask<MyTask>())
```

---

## Adding a new DotflowEvent

Events are plain records that inherit `DotflowEvent`:

```csharp
public record MyTaskCompletedEvent : DotflowEvent
{
    public required string WorkflowRunId { get; init; }
    public string? SomePayload { get; init; }
}
```

Trigger a phase on this event:

```csharp
.AddPhase("next-phase", phase => phase
    .TriggeredOn<MyTaskCompletedEvent>()
    .AddTask<FollowUpTask>())
```

The engine subscribes to `IEventBus` and matches event types to `OnEvent<T>` triggers via `IsAssignableFrom`, so event hierarchies work too.

---

## Adding a new persistence provider

1. Create a class implementing `IPipelineStore` in a new project.
2. Expose a registration extension:

```csharp
public static IServiceCollection UseMyStore(this IServiceCollection services, string connectionString)
{
    services.AddSingleton<IPipelineStore>(_ => new MyPipelineStore(connectionString));
    return services;
}
```

3. Register it before `AddDotflow` (or alongside it), ensuring only one `IPipelineStore` is registered.

See `src/dotflow.Persistence.InMemory/InMemoryPipelineStore.cs` for the simplest reference implementation.

---

## Adding a concurrent group to a phase

```csharp
.AddPhase("enrichment", phase => phase
    .TriggeredImmediately()
    .AddTask<FetchUserTask>()               // sequential
    .AddConcurrentGroup(group => group      // parallel
        .AddTask<FetchOrdersTask>()
        .AddTask<FetchInventoryTask>())
    .AddTask<AggregateTask>())              // waits for the group above
```

`ConcurrentGroup` tasks run via `Task.WhenAll`. Execution continues to the next slot only after all group tasks complete.

---

## Adding phase-level resiliency

Override global resiliency for a specific phase:

```csharp
.AddPhase("flaky-phase", phase => phase
    .TriggeredImmediately()
    .AddTask<FlakyTask>()
    .WithResiliency(new ResiliencyOptions
    {
        Retry = new RetryOptions
        {
            MaxAttempts = 5,
            Strategy = RetryStrategy.ExponentialBackoff,
            BaseDelay = TimeSpan.FromSeconds(2)
        },
        Timeout = new TimeoutOptions { Timeout = TimeSpan.FromMinutes(2) }
    }))
```

---

## Adding a new dashboard page

`DashboardMiddleware.ServePageAsync` dispatches by `subPath`. To add a new page:

1. Add a new case to the `switch` in `ServePageAsync`.
2. Add a `Build*Page` static method returning an HTML string.
3. Add a matching API endpoint handler if the page needs data.

HTMX polling works by adding `hx-get="<api-path>" hx-trigger="load, every Ns"` to a container div.

---

## Registering an event handler outside of a phase trigger

Use `IEventBus.Subscribe<T>` directly (resolved from DI):

```csharp
public class MyStartupService : IHostedService
{
    private readonly IEventBus _bus;
    private IDisposable? _sub;

    public MyStartupService(IEventBus bus) => _bus = bus;

    public Task StartAsync(CancellationToken ct)
    {
        _sub = _bus.Subscribe<MyTaskCompletedEvent>(async (e, token) =>
        {
            // react to event outside the workflow engine
        });
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) { _sub?.Dispose(); return Task.CompletedTask; }
}
```

---

## Loading workflows from configuration (JSON)

```csharp
dotflow.LoadFromConfiguration(builder.Configuration.GetSection("dotflow"));
```

Not yet implemented — `DotflowBuilder` currently only supports the fluent API. To implement, add a method to `DotflowBuilder` that reads `IConfiguration` and maps to `WorkflowDefinition` / `PhaseDefinition` objects. Task types would need to be resolved by assembly-qualified name.

---

## Loading workflows from YAML

```csharp
services.AddDotflow(dotflow => dotflow
    .LoadFromYaml(File.ReadAllText("workflows.yaml")));
```

Requires `dotflow.Extensions.Yaml`. The YAML schema matches `DotflowYamlConfig`. Task types are currently not wired from YAML (implement by resolving `Type.GetType(assemblyQualifiedName)` in `YamlConfigurationExtensions`).
