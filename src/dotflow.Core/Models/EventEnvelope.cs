namespace Dotflow.Models;

public class EventEnvelope
{
    public string Id { get; init; } = Ulid.NewUlid().ToString();
    public required string RunId { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
