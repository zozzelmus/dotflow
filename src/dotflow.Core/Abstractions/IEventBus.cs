using Dotflow.Models;

namespace Dotflow.Abstractions;

public interface IEventBus
{
    System.Threading.Tasks.Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DotflowEvent;

    IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, System.Threading.Tasks.Task> handler)
        where TEvent : DotflowEvent;
}
