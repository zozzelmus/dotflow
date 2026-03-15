namespace Dotflow.Models;

public class PhaseRun
{
    public string Id { get; init; } = Ulid.NewUlid().ToString();
    public required string PhaseName { get; init; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<TaskRun> Tasks { get; set; } = new();
}
