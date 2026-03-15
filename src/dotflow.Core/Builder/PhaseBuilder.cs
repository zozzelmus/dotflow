using Dotflow.Abstractions;
using Dotflow.Configuration;
using Dotflow.Models;

namespace Dotflow.Builder;

public class PhaseBuilder
{
    private readonly PhaseDefinition _definition;

    internal PhaseBuilder(string name)
    {
        _definition = new PhaseDefinition { Name = name };
    }

    public PhaseBuilder TriggeredImmediately()
    {
        _definition.Trigger = new PhaseTrigger.Immediate();
        return this;
    }

    public PhaseBuilder TriggeredOn<TEvent>() where TEvent : DotflowEvent
    {
        _definition.Trigger = new OnEvent<TEvent>();
        return this;
    }

    public PhaseBuilder AddTask<T>() where T : DotflowTask
    {
        _definition.Tasks.Add(new TaskSlot.SingleTask(typeof(T)));
        return this;
    }

    public PhaseBuilder AddConcurrentGroup(Action<ConcurrentGroupBuilder> configure)
    {
        var groupBuilder = new ConcurrentGroupBuilder();
        configure(groupBuilder);
        _definition.Tasks.Add(groupBuilder.Build());
        return this;
    }

    public PhaseBuilder WithResiliency(ResiliencyOptions options)
    {
        _definition.Resiliency = options;
        return this;
    }

    public PhaseBuilder ContinueOnFailure()
    {
        _definition.ContinueOnFailure = true;
        return this;
    }

    internal PhaseDefinition Build() => _definition;
}
