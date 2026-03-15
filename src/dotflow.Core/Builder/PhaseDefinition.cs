using Dotflow.Configuration;

namespace Dotflow.Builder;

public class PhaseDefinition
{
    public required string Name { get; init; }
    public PhaseTrigger? Trigger { get; set; }
    public List<TaskSlot> Tasks { get; init; } = new();
    public ResiliencyOptions? Resiliency { get; set; }
    public bool ContinueOnFailure { get; set; }
}
