# CLAUDE.md

This file provides guidance to Claude Code when working with this repository.

## Commands

```bash
dotnet build                    # Build entire solution (debug)
dotnet build -c Release         # Build (release)
dotnet test dotflow.sln         # Run all tests (19 unit + 1 skippable integration)
dotnet run --project samples/dotflow.Sample.Basic    # Run end-to-end demo
dotnet run --project samples/dotflow.Sample.WebApi   # Run web host with dashboard
dotnet clean                    # Clean build artifacts
```

Integration tests require `DOTFLOW_PG_CONN` env var pointing at a PostgreSQL instance. They skip automatically without it.

## Project

.NET 10 solution (`net10.0`). 10 projects: Core engine, InMemory + PostgreSQL persistence, Dashboard, MediatR + YAML extensions, 2 samples, 2 test projects. Nullable reference types and implicit usings enabled throughout.

The root `dotflow.csproj` / `Program.cs` are legacy stubs from the initial scaffold — ignore them.

## Documentation

Detailed context is in `/docs/`. Read the relevant doc before making significant changes.

| Doc | When to read |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Overview of the three-level hierarchy, project map, key files, event system, RunTracker internals, resiliency, persistence, dashboard |
| [`docs/adding-features.md`](docs/adding-features.md) | How to add tasks, events, persistence providers, concurrent groups, dashboard pages |
| [`docs/decisions.md`](docs/decisions.md) | Why synchronous event dispatch, why no EF, JSONB choice, RunTracker design, etc. Read before changing core engine patterns |
| [`docs/known-gaps.md`](docs/known-gaps.md) | Not-yet-implemented features, known limitations, planned extensions — check here before implementing something that might already be partially designed |

## Key conventions

- **Task naming:** Base class is `DotflowTask` (not `Task`) to avoid shadowing `System.Threading.Tasks.Task`. See `docs/decisions.md`.
- **No EF Core:** `dotflow.Persistence.PostgreSQL` uses Dapper + Npgsql only.
- **Event dispatch is synchronous:** `InternalEventBus.PublishAsync` awaits all handlers before returning. Critical for RunTracker correctness — do not change without reading `docs/decisions.md`.
- **IPipelineStore is the only persistence interface.** All reads and writes go through it; the dashboard reads from it too.
- **Startup validation** runs in `DotflowHostedService.StartAsync` and throws `DotflowValidationException` for any misconfiguration.
- **Comments:** Only on complex logic. Don't add docstrings or comments to straightforward code.
