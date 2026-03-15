using Dotflow.Builder;

namespace Dotflow.Configuration;

public class DotflowOptions
{
    public ResiliencyOptions? Resiliency { get; set; }
    public List<WorkflowDefinition> Workflows { get; set; } = new();
}
