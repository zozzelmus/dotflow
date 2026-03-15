using Dotflow.Abstractions;
using Dotflow.Models;
using MediatR;

namespace Dotflow.Extensions.MediatR;

/// <summary>
/// IEventBus implementation that bridges DotflowEvents to MediatR INotification.
/// Register via services.AddSingleton&lt;IEventBus, MediatREventBus&gt;() after AddMediatR().
/// </summary>
public sealed class MediatREventBus : IEventBus
{
    private readonly IMediator _mediator;

    public MediatREventBus(IMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DotflowEvent
    {
        if (@event is INotification notification)
            await _mediator.Publish(notification, ct);
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : DotflowEvent
    {
        // MediatR uses compile-time handler registration; runtime subscriptions are not supported.
        // Implement INotificationHandler<TEvent> directly and register with DI instead.
        throw new NotSupportedException(
            "Runtime event subscriptions are not supported with MediatR. " +
            "Implement INotificationHandler<T> and register with services.AddMediatR().");
    }
}
