# Design Decisions

Record of significant design choices and the reasoning behind them. Update this when making architectural changes.

---

## Synchronous event dispatch in InternalEventBus

**Decision:** `PublishAsync` awaits all subscriber handlers before returning, rather than queuing to a `System.Threading.Channels.Channel` for async dispatch.

**Why:** The `WorkflowEngine` uses a per-run active-phase counter (`RunTracker._activePhases`) to know when all phases have completed. If event dispatch were async (channel-based), there would be a race: a task publishes an event and returns, the publishing phase finishes and decrements the counter to zero, the workflow marks itself `Succeeded` — but the channel hasn't delivered the event yet, so the event-triggered phase never starts.

Synchronous dispatch means that by the time a task's `PublishAsync` call returns, the engine's event subscriber has already incremented the active-phase counter for the next phase. The counter can never drop to zero prematurely.

**Trade-off:** Long-running subscribers will block the publishing task. For dotflow's internal subscriber (which does an `await store.GetRunAsync` then fires a phase as a background task), this is fast. External subscribers added by users via `Subscribe<T>` should be kept lightweight.

---

## DotflowTask naming

**Decision:** The base class is `DotflowTask`, not `Task`.

**Why:** `Task` shadows `System.Threading.Tasks.Task`, which is used pervasively throughout .NET. Files that subclass it would need an alias for the BCL type. The type is in the `Dotflow` namespace so it appears as `Dotflow.Task` in user code, which is readable.

**Alternative considered:** Rename to `Activity` (like Temporal). Rejected as `Task` is more natural for this domain.

---

## JSONB for phases/tasks in PostgreSQL

**Decision:** `WorkflowRun` rows store the full `phases` tree as a JSONB column rather than separate `phase_runs` and `task_runs` tables.

**Why:** Avoids JOIN complexity on reads. The dashboard's main query is "give me the run with all its phase/task state" — a single row fetch. Partial updates (updating phase status during execution) overwrite the JSONB column with the full run state. This is acceptable since runs are short-lived and the serialized state is small.

**Trade-off:** Can't efficiently query "all tasks named X across all runs" without JSONB path queries. Acceptable for the current feature set.

---

## HTMX over SSE/WebSocket in the dashboard

**Decision:** Dashboard uses HTMX polling (`hx-trigger="every Ns"`) rather than Server-Sent Events or WebSocket.

**Why:** SSE and WebSocket require sticky sessions or a shared message broker in multi-container deployments. HTMX polling works with any load balancer since every request is stateless and reads from the shared `IPipelineStore`.

**Trade-off:** 3–10 second polling lag vs. true push. Acceptable for an operations dashboard.

---

## IPipelineStore as the only persistence abstraction

**Decision:** A single `IPipelineStore` interface covering all read/write operations, rather than separate read and write interfaces (CQRS split).

**Why:** Simplifies implementation. The expected number of store implementations is small (InMemory, PostgreSQL, future SQL Server/SQLite). A CQRS split would double the interface surface without clear benefit at this scale.

---

## No EF Core in dotflow.Persistence.PostgreSQL

**Decision:** Dapper + Npgsql only; no Entity Framework.

**Why:** Open-source readability. SQL is explicit and reviewable by contributors without ORM knowledge. EF migrations add indirection; the schema is simple enough to maintain in a single `Schema.sql` file. Dapper's thin mapping layer is sufficient given the JSONB-heavy schema.

---

## RunTracker active-phase counter approach

**Decision:** `WorkflowEngine` uses a per-run `RunTracker` with an `int` counter rather than collecting all phase `Task` objects and awaiting them with `Task.WhenAll`.

**Why:** Event-triggered phases are dynamically added at runtime (when events arrive). You can't `Task.WhenAll` a collection that keeps growing. The counter approach handles dynamic addition naturally: increment before start, decrement in `finally`, set completion when counter == 0 and all immediates have been submitted.

---

## Startup validation via IHostedService

**Decision:** `WorkflowValidator` runs in `DotflowHostedService.StartAsync`, which means invalid configurations cause the host to fail on startup before any traffic is served.

**Why:** Fail-fast is better than discovering a misconfigured workflow when it's first triggered in production. The validator catches: missing triggers, empty phases, unregistered task types in DI, duplicate workflow IDs.

---

## SetOutput writes immediately to sharedContext

**Decision:** `TaskContext.SetOutput` writes to both the per-task `Output` dict AND `RunTracker.Context` (`sharedContext`) in the same call, before returning.

**Why:** Events are published from within a task via `context.Events.PublishAsync`. Since `InternalEventBus` dispatches synchronously, the engine's event subscriber (which starts the next phase) runs within the same call stack as `PublishAsync`. If `sharedContext` were only updated after `_taskExecutor.ExecuteAsync` returned, the next phase's input snapshot would be taken before the outputs were written — the value would be missing.

Writing immediately in `SetOutput` ensures outputs are in `sharedContext` before any event is published, regardless of task implementation.

**Trade-off:** `TaskContext.Output` (returned in `TaskRun`) contains only this task's own writes, while `sharedContext` accumulates all writes across all tasks. The per-task dict is used for `TaskRun.Output` attribution; the shared dict is used for input construction. Both are needed.

---

## No config-file workflow loading (yet)

**Decision:** `DotflowBuilder.LoadFromConfiguration` is documented but not implemented. Only the fluent API is complete.

**Why:** Config-file loading requires task type resolution by assembly-qualified name, which needs more design (security implications, error messages, partial trust). The YAML extension shows the pattern but also leaves task type wiring as a TODO. Implement when there is a concrete user need.
