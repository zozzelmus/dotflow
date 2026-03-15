namespace Dotflow.Models;

public class RunQuery
{
    public string? WorkflowId { get; init; }
    public RunStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}
