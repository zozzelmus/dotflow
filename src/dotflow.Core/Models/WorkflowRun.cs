namespace Dotflow.Models;

public class WorkflowRun
{
    public string Id { get; init; } = Ulid.NewUlid().ToString();
    public required string WorkflowId { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public Dictionary<string, object?> Input { get; set; } = new();
    public List<PhaseRun> Phases { get; set; } = new();
}
