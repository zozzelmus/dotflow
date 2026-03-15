using System.Text.Json;
using Dapper;
using Dotflow.Abstractions;
using Dotflow.Models;
using Npgsql;

namespace Dotflow.Persistence.PostgreSQL;

public sealed class PostgreSQLPipelineStore : IPipelineStore
{
    private readonly string _connectionString;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PostgreSQLPipelineStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    public async Task SaveRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dotflow.workflow_runs (id, workflow_id, status, created_at, started_at, finished_at, input, phases)
            VALUES (@Id, @WorkflowId, @Status, @CreatedAt, @StartedAt, @FinishedAt, @Input::jsonb, @Phases::jsonb)
            """;

        await using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            run.Id,
            run.WorkflowId,
            Status = run.Status.ToString(),
            run.CreatedAt,
            run.StartedAt,
            run.FinishedAt,
            Input = JsonSerializer.Serialize(run.Input, JsonOptions),
            Phases = JsonSerializer.Serialize(run.Phases, JsonOptions)
        });
    }

    public async Task UpdateRunAsync(WorkflowRun run, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE dotflow.workflow_runs
            SET status = @Status, started_at = @StartedAt, finished_at = @FinishedAt,
                phases = @Phases::jsonb
            WHERE id = @Id
            """;

        await using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            run.Id,
            Status = run.Status.ToString(),
            run.StartedAt,
            run.FinishedAt,
            Phases = JsonSerializer.Serialize(run.Phases, JsonOptions)
        });
    }

    public async Task AppendEventAsync(string runId, EventEnvelope evt, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO dotflow.events (id, run_id, event_type, payload, occurred_at)
            VALUES (@Id, @RunId, @EventType, @Payload::jsonb, @OccurredAt)
            """;

        await using var conn = CreateConnection();
        await conn.ExecuteAsync(sql, new
        {
            evt.Id,
            evt.RunId,
            evt.EventType,
            evt.Payload,
            evt.OccurredAt
        });
    }

    public async Task<WorkflowRun?> GetRunAsync(string runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, workflow_id, status, created_at, started_at, finished_at, input, phases
            FROM dotflow.workflow_runs
            WHERE id = @RunId
            """;

        await using var conn = CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync(sql, new { RunId = runId });
        return row is null ? null : MapRun(row);
    }

    public async Task<PagedResult<WorkflowRun>> ListRunsAsync(RunQuery query, CancellationToken ct = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (query.WorkflowId is not null)
        {
            conditions.Add("workflow_id = @WorkflowId");
            parameters.Add("WorkflowId", query.WorkflowId);
        }
        if (query.Status.HasValue)
        {
            conditions.Add("status = @Status");
            parameters.Add("Status", query.Status.Value.ToString());
        }
        if (query.From.HasValue)
        {
            conditions.Add("created_at >= @From");
            parameters.Add("From", query.From.Value);
        }
        if (query.To.HasValue)
        {
            conditions.Add("created_at <= @To");
            parameters.Add("To", query.To.Value);
        }

        var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var offset = (query.Page - 1) * query.PageSize;
        parameters.Add("Limit", query.PageSize);
        parameters.Add("Offset", offset);

        var countSql = $"SELECT COUNT(*) FROM dotflow.workflow_runs {where}";
        var dataSql = $"""
            SELECT id, workflow_id, status, created_at, started_at, finished_at, input, phases
            FROM dotflow.workflow_runs {where}
            ORDER BY created_at DESC
            LIMIT @Limit OFFSET @Offset
            """;

        await using var conn = CreateConnection();
        var total = await conn.QuerySingleAsync<int>(countSql, parameters);
        var rows = await conn.QueryAsync(dataSql, parameters);
        var items = rows.Select(r => MapRun(r)).ToList() as IReadOnlyList<WorkflowRun>;

        return new PagedResult<WorkflowRun>
        {
            Items = items,
            TotalCount = total,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<IReadOnlyList<EventEnvelope>> GetRunEventsAsync(string runId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, run_id, event_type, payload, occurred_at
            FROM dotflow.events
            WHERE run_id = @RunId
            ORDER BY occurred_at ASC
            """;

        await using var conn = CreateConnection();
        var rows = await conn.QueryAsync(sql, new { RunId = runId });
        return rows.Select(r => new EventEnvelope
        {
            Id = r.id,
            RunId = r.run_id,
            EventType = r.event_type,
            Payload = r.payload,
            OccurredAt = r.occurred_at
        }).ToList();
    }

    public async Task<RunStats> GetStatsAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE status = 'Succeeded') AS succeeded,
                COUNT(*) FILTER (WHERE status = 'Failed') AS failed,
                COUNT(*) FILTER (WHERE status = 'Running') AS running,
                AVG(EXTRACT(EPOCH FROM (finished_at - started_at)) * 1000)
                    FILTER (WHERE finished_at IS NOT NULL AND started_at IS NOT NULL) AS avg_ms
            FROM dotflow.workflow_runs
            """;

        await using var conn = CreateConnection();
        var row = await conn.QuerySingleAsync(sql);

        return new RunStats
        {
            TotalRuns = (int)row.total,
            SucceededRuns = (int)row.succeeded,
            FailedRuns = (int)row.failed,
            RunningRuns = (int)row.running,
            AverageDuration = row.avg_ms is not null
                ? TimeSpan.FromMilliseconds((double)row.avg_ms)
                : null
        };
    }

    private static WorkflowRun MapRun(dynamic row)
    {
        var run = new WorkflowRun
        {
            Id = (string)row.id,
            WorkflowId = (string)row.workflow_id
        };
        run.Status = Enum.Parse<RunStatus>((string)row.status);
        run.StartedAt = row.started_at;
        run.FinishedAt = row.finished_at;
        run.Input = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            (string)row.input, JsonOptions) ?? [];
        run.Phases = JsonSerializer.Deserialize<List<PhaseRun>>(
            (string)row.phases, JsonOptions) ?? [];
        return run;
    }
}
