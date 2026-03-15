using Dotflow.Models;

namespace Dotflow.Builder;

public abstract record PhaseTrigger
{
    public sealed record Immediate : PhaseTrigger;

    public sealed record OnEvent(string EventType) : PhaseTrigger;

    public static OnEvent<TEvent> For<TEvent>() where TEvent : DotflowEvent
        => new();
}

public sealed record OnEvent<TEvent>() : PhaseTrigger
    where TEvent : DotflowEvent
{
    public string EventType => typeof(TEvent).FullName ?? typeof(TEvent).Name;
}
