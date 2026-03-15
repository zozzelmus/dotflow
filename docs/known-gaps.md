# Known Gaps & Future Work

Items that are designed but not fully implemented, or known limitations to address.

---

## Not implemented

### Config-file / JSON workflow loading
`DotflowBuilder.LoadFromConfiguration(IConfiguration)` is referenced in the architecture plan but not implemented. The fluent builder is the only way to register workflows today. See `docs/adding-features.md` → "Loading workflows from configuration" for implementation notes.

### YAML task type wiring
`dotflow.Extensions.Yaml` parses workflow/phase structure from YAML but does not resolve task types from `type: "MyApp.Tasks.Foo, MyApp"` strings. Requires `Type.GetType(assemblyQualifiedName)` + DI registration validation.

### WorkflowValidator: orphaned event trigger detection
The validator checks for missing triggers, empty phases, and unregistered DI types. It does NOT yet detect:
- Orphaned `OnEvent<T>` triggers where no other phase ever publishes `T` (requires static task metadata analysis)
- Circular event chains (A publishes event → triggers B, B publishes event → triggers A)

The DFS cycle detection stub in `WorkflowValidator.DetectCycle` is a placeholder.

### ~~Phase output propagation~~ ✓ Implemented
Task outputs are now propagated via a `ConcurrentDictionary` (`RunTracker.Context`) shared across all phases in a run. `TaskContext.SetOutput` writes to both the per-task `Output` dict (for `TaskRun` attribution) and `sharedContext` immediately — so outputs are visible to event-triggered phases that start within the same `PublishAsync` call. Sequential tasks within a phase see each other's outputs; concurrent group tasks see a pre-group snapshot.

### dotflow migrate CLI command
The architecture plan describes a `DotflowTask migrate` CLI command (`System.CommandLine`) for running schema migrations. Currently only `Migrator.MigrateAsync(connectionString)` exists as a static method. The CLI wrapper is not implemented.

### Dashboard authentication
`DashboardOptions.RequireAuthentication` and `AuthorizationFilter` fields exist but the middleware's auth check has a logic bug (double negation). When implementing properly, integrate with ASP.NET Core's `IAuthorizationService`.

### Dashboard: workflow list page
`/dotflow/workflows` shows recent runs but does not show the list of registered workflow definitions with their last-run status. Requires injecting `IReadOnlyList<WorkflowDefinition>` into the middleware.

---

## Known limitations

### InMemoryPipelineStore is not multi-container-safe
Documented in `InMemoryPersistenceExtensions.cs`. Use `dotflow.Persistence.PostgreSQL` in any multi-instance deployment.

### InternalEventBus is in-process only
Events published via `IEventBus` are not durable and not distributed. If a process crashes mid-workflow, event-triggered phases that haven't started yet are lost. A durable event bus (e.g., backed by PostgreSQL NOTIFY or a message broker) is needed for production resilience.

### No workflow run recovery on restart
If the host restarts while runs are `Running`, they stay in `Running` status in the store indefinitely. There is no checkpoint/resume mechanism. On restart, the engine should query `IPipelineStore` for stuck runs and either mark them `Failed` or attempt recovery.

### ConcurrentGroup task failures
If one task in a `ConcurrentGroup` fails, `Task.WhenAll` still waits for the others to complete before the failure is propagated. `ContinueOnFailure` at the phase level applies after all concurrent tasks finish. There is no "fail-fast" option for concurrent groups.

### No workflow-level timeout
`ResiliencyOptions.Timeout` applies per-task. There is no overall workflow-level deadline. Implement by passing a derived `CancellationToken` with a deadline into `ExecuteWorkflowAsync`.

---

## Planned extensions (not started)

- `dotflow.Persistence.SQLite` — for single-binary/embedded use cases
- `dotflow.Persistence.SqlServer` — for Microsoft SQL Server
- Workflow versioning — running multiple versions of the same workflow ID concurrently
- Scheduled triggers — `TriggeredOnSchedule(cronExpression)` using `IHostedService` + NCrontab
- Dashboard dark mode and better run timeline visualization
