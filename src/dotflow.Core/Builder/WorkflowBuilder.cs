using Dotflow.Configuration;

namespace Dotflow.Builder;

public class WorkflowBuilder
{
    private readonly WorkflowDefinition _definition;

    internal WorkflowBuilder(string id)
    {
        _definition = new WorkflowDefinition { Id = id };
    }

    public WorkflowBuilder WithName(string name)
    {
        _definition.Name = name;
        return this;
    }

    public WorkflowBuilder AddPhase(string name, Action<PhaseBuilder> configure)
    {
        var phaseBuilder = new PhaseBuilder(name);
        configure(phaseBuilder);
        _definition.Phases.Add(phaseBuilder.Build());
        return this;
    }

    public WorkflowBuilder WithResiliency(ResiliencyOptions options)
    {
        _definition.Resiliency = options;
        return this;
    }

    public WorkflowBuilder WithTimeout(TimeSpan timeout)
    {
        _definition.Timeout = timeout;
        return this;
    }

    internal WorkflowDefinition Build() => _definition;
}
