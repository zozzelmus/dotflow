# Architecture

## Overview

dotflow is a .NET 10 workflow engine for building event-driven ETL/data pipelines. The core concept is a three-level hierarchy: **Workflow → Phase → Task**, where phases are wired together via events rather than explicit dependencies.

## Three-Level Hierarchy

```
Workflow              top-level orchestration unit; identified by string ID
  └── Phase[]         named sequence of task slots; has exactly one trigger
        └── TaskSlot  either a SingleTask or a ConcurrentGroup (run in parallel)
              └── DotflowTask   atomic unit of work; users subclass this
```

### Execution model

- **Immediate** phases all start concurrently when the workflow is triggered.
- **OnEvent\<T\>** phases start when a matching event is published to the event bus.
- Within a phase, `TaskSlot`s execute sequentially; `ConcurrentGroup` slots fan out with `Task.WhenAll`.
- The event graph IS the dependency graph — no explicit ordering declarations needed.

### Run state machine

```
Pending → Running → Succeeded
                  → Failed
                  → Cancelled
                  → TimedOut
```

Failed task propagates `Failed` upward (phase → run) unless `ContinueOnFailure` is set on the phase.

---

## Project Map

| Project | Namespace | Role |
|---|---|---|
| `src/dotflow.Core` | `Dotflow` | Abstractions, engine, events, validation, DI extensions |
| `src/dotflow.Persistence.InMemory` | `Dotflow.Persistence.InMemory` | Dev/test store; **single-instance only** |
| `src/dotflow.Persistence.PostgreSQL` | `Dotflow.Persistence.PostgreSQL` | Production store; Dapper + Npgsql + JSONB |
| `src/dotflow.Dashboard` | `Dotflow.Dashboard` | ASP.NET middleware + HTMX UI |
| `src/dotflow.Extensions.MediatR` | `Dotflow.Extensions.MediatR` | Optional IEventBus → MediatR bridge |
| `src/dotflow.Extensions.Yaml` | `Dotflow.Extensions.Yaml` | Optional YAML config loader |
| `samples/dotflow.Sample.Basic` | `Dotflow.Sample.Basic` | Console end-to-end demo |
| `samples/dotflow.Sample.WebApi` | `Dotflow.Sample.WebApi` | Web host with dashboard |
| `tests/dotflow.Core.Tests` | `Dotflow.Core.Tests` | Unit tests |
| `tests/dotflow.Integration.Tests` | `Dotflow.Integration.Tests` | PostgreSQL integration tests |

---

## Key Files

### Core engine

| File | Purpose |
|---|---|
| `src/dotflow.Core/Engine/WorkflowEngine.cs` | Orchestrator. Manages per-run `RunTracker` (active phase counter + completion source). Subscribes to `IEventBus` to start event-triggered phases. **Thread-safety critical.** |
| `src/dotflow.Core/Engine/PhaseExecutor.cs` | Iterates `TaskSlot`s sequentially; fans out `ConcurrentGroup` with `Task.WhenAll`. |
| `src/dotflow.Core/Engine/TaskExecutor.cs` | Wraps each task execution in a Polly `ResiliencePipeline`. Tracks attempt count. |
| `src/dotflow.Core/Engine/TaskContext.cs` | Runtime view injected into each task: IDs, input/output bag, event bus, logger, CT. |
| `src/dotflow.Core/Engine/ResiliencyPolicyFactory.cs` | Translates `ResiliencyOptions` → Polly `ResiliencePipeline`. |
| `src/dotflow.Core/Engine/DotflowHostedService.cs` | `IHostedService` that runs `WorkflowValidator` on startup before any trigger is possible. |

### Abstractions

| File | Interface |
|---|---|
| `src/dotflow.Core/Abstractions/DotflowTask.cs` | Base class for all tasks. Note: named `DotflowTask` to avoid shadowing `System.Threading.Tasks.Task`. |
| `src/dotflow.Core/Abstractions/ITaskContext.cs` | What a task sees at runtime. |
| `src/dotflow.Core/Abstractions/IEventBus.cs` | Publish/subscribe for `DotflowEvent`. |
| `src/dotflow.Core/Abstractions/IPipelineStore.cs` | Persistence abstraction; all store implementations fulfill this. |
| `src/dotflow.Core/Abstractions/IWorkflowEngine.cs` | `TriggerAsync` / `CancelAsync`. |

### Builder

| File | Purpose |
|---|---|
| `src/dotflow.Core/Builder/DotflowBuilder.cs` | Root builder returned by `AddDotflow(...)`. |
| `src/dotflow.Core/Builder/WorkflowBuilder.cs` | Fluent API for defining workflows. |
| `src/dotflow.Core/Builder/PhaseBuilder.cs` | Fluent API for defining phases: trigger, tasks, resiliency. |
| `src/dotflow.Core/Builder/ConcurrentGroupBuilder.cs` | Fluent API for concurrent task groups. |
| `src/dotflow.Core/Builder/WorkflowDefinition.cs` | Immutable definition of a workflow (registered at startup). |
| `src/dotflow.Core/Builder/PhaseDefinition.cs` | Immutable definition of a phase. |
| `src/dotflow.Core/Builder/TaskSlot.cs` | Discriminated union: `SingleTask` or `ConcurrentGroup`. |
| `src/dotflow.Core/Builder/PhaseTrigger.cs` | Discriminated union: `Immediate`, `OnEvent(string)`, `OnEvent<T>`. |

### Models (persisted state)

| File | Purpose |
|---|---|
| `src/dotflow.Core/Models/WorkflowRun.cs` | Top-level run record. ULID ID. |
| `src/dotflow.Core/Models/PhaseRun.cs` | Per-phase run state. |
| `src/dotflow.Core/Models/TaskRun.cs` | Per-task run state. Tracks `AttemptCount`. |
| `src/dotflow.Core/Models/DotflowEvent.cs` | Base record for all events. |
| `src/dotflow.Core/Models/EventEnvelope.cs` | Serialized event stored in the pipeline store. |
| `src/dotflow.Core/Models/RunQuery.cs` | Filter/pagination for `IPipelineStore.ListRunsAsync`. |
| `src/dotflow.Core/Models/RunStats.cs` | Aggregate statistics returned by `IPipelineStore.GetStatsAsync`. |
| `src/dotflow.Core/Models/PagedResult.cs` | Generic paged list. |
| `src/dotflow.Core/Models/RunStatus.cs` | `Pending / Running / Succeeded / Failed / Cancelled / TimedOut`. |

---

## Input/Output Propagation

Each task receives a merged `Input` view: the original workflow input overlaid with all outputs written by prior tasks across all phases. Tasks call `context.SetOutput(key, value)`, which writes to both the per-task `Output` dict (stored in `TaskRun.Output`) and a `ConcurrentDictionary` (`RunTracker.Context`) shared across the entire run.

**Key timing guarantee:** `SetOutput` writes to `sharedContext` immediately — before any event is published within the same task call. This means an event-triggered phase that starts synchronously within `PublishAsync` will already see the output in its input snapshot.

Within a phase:
- **Sequential tasks** — each gets a fresh `Merge(originalInput, sharedContext)` snapshot when it starts; it sees every output written by earlier tasks.
- **Concurrent group tasks** — all share a single snapshot taken before the group starts; they see the same input, and their independent outputs are each merged into `sharedContext` as they complete.

---

## Event System

`InternalEventBus` dispatches **synchronously** within `PublishAsync` — all subscribers are awaited before `PublishAsync` returns. This is critical for the `WorkflowEngine`'s phase-counter bookkeeping: when a task publishes an event, the engine's subscriber increments the active-phase counter *before* the publishing task returns, eliminating the race where the counter could drop to zero prematurely.

The MediatR bridge (`dotflow.Extensions.MediatR`) replaces `InternalEventBus` with `MediatREventBus`. It only supports `PublishAsync`; runtime `Subscribe` is not supported — use `INotificationHandler<T>` instead.

---

## RunTracker (WorkflowEngine internals)

Each active `WorkflowRun` has a `RunTracker` instance (private nested class in `WorkflowEngine`):

```
RunTracker
  ├── CancellationTokenSource  — for cancel propagation
  ├── _activePhases (int)      — incremented before each phase starts, decremented in finally
  ├── _immediatesPending (bool)— set after all immediate phases have been submitted
  └── TaskCompletionSource     — completes when _immediatesPending && _activePhases == 0
```

`ExecuteWorkflowAsync` awaits `tracker.CompletionTask`, which only resolves when every phase (immediate + event-triggered) has finished.

---

## Resiliency

Polly v8 (`Microsoft.Extensions.Resilience`). Three levels of configuration — most specific wins:

```
Global (DotflowBuilder.ConfigureResiliency)
  └── Workflow (WorkflowBuilder.WithResiliency)
        └── Phase (PhaseBuilder.WithResiliency)
```

`ResiliencyPolicyFactory.Build(options)` constructs the pipeline in this order: Timeout → CircuitBreaker → Retry (outermost → innermost).

---

## Persistence

`IPipelineStore` is the only persistence interface. `WorkflowRun` stores phases and tasks as nested objects (JSONB in PostgreSQL), avoiding JOIN complexity.

PostgreSQL schema is in `src/dotflow.Persistence.PostgreSQL/Migrations/Schema.sql` (embedded resource). Apply it via `Migrator.MigrateAsync(connectionString)`.

---

## Dashboard

`DashboardMiddleware` mounts at a configurable path prefix (default `/dotflow`). It handles both the HTML pages and the JSON API used by HTMX partials. HTMX polls the API endpoints periodically — no SSE or WebSocket required, which makes it safe in multi-container/load-balanced deployments.

All data reads go through `IPipelineStore`, so any container running the dashboard reads the same data as the containers executing workflows (as long as they share a store).
