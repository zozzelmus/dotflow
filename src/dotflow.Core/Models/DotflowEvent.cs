namespace Dotflow.Models;

public abstract record DotflowEvent
{
    public string EventId { get; init; } = Ulid.NewUlid().ToString();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string? CorrelationId { get; init; }
    public string? WorkflowRunId { get; init; }
}
