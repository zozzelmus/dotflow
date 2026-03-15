using Dotflow.Configuration;

namespace Dotflow.Builder;

public class WorkflowDefinition
{
    public required string Id { get; init; }
    public string? Name { get; set; }
    public List<PhaseDefinition> Phases { get; init; } = new();
    public ResiliencyOptions? Resiliency { get; set; }
}
