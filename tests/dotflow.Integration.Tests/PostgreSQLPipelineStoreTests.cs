using Dotflow.Models;
using Dotflow.Persistence.PostgreSQL;
using Xunit;

namespace Dotflow.Integration.Tests;

/// <summary>
/// Integration tests for PostgreSQLPipelineStore.
/// Requires a running PostgreSQL instance and the DOTFLOW_PG_CONN environment variable.
/// Run: DOTFLOW_PG_CONN="Host=localhost;Database=dotflow_test;Username=dotflow;Password=dotflow" dotnet test
/// Before running: apply the schema via Migrator.MigrateAsync(connectionString).
/// </summary>
public class PostgreSQLPipelineStoreTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("DOTFLOW_PG_CONN");

    private bool CanRun => !string.IsNullOrEmpty(ConnectionString);

    [SkippableFact]
    public async Task SaveAndGetRun_RoundTrips()
    {
        Skip.IfNot(CanRun, "DOTFLOW_PG_CONN not set — skipping PostgreSQL integration tests.");

        await Migrator.MigrateAsync(ConnectionString!);
        var store = new PostgreSQLPipelineStore(ConnectionString!);

        var run = new WorkflowRun { WorkflowId = "integration-test" };
        await store.SaveRunAsync(run);

        var retrieved = await store.GetRunAsync(run.Id);

        Assert.NotNull(retrieved);
        Assert.Equal(run.Id, retrieved.Id);
        Assert.Equal("integration-test", retrieved.WorkflowId);
    }
}
