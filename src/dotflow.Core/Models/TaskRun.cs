namespace Dotflow.Models;

public class TaskRun
{
    public string Id { get; init; } = Ulid.NewUlid().ToString();
    public required string TaskName { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object?> Output { get; set; } = new();
}
