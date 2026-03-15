using Dotflow.Abstractions;
using Dotflow.Models;
using Microsoft.Extensions.Logging;

namespace Dotflow.Events;

/// <summary>
/// Default in-process event bus. Dispatches to subscribers synchronously within PublishAsync,
/// so callers can rely on all handlers having run before PublishAsync returns.
/// </summary>
internal sealed class InternalEventBus : IEventBus
{
    private readonly ILogger<InternalEventBus> _logger;
    private readonly List<(Type EventType, Func<DotflowEvent, CancellationToken, Task> Handler)> _handlers = [];
    private readonly Lock _handlersLock = new();

    public InternalEventBus(ILogger<InternalEventBus> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : DotflowEvent
    {
        _logger.LogDebug("Publishing event {EventType} ({EventId})", typeof(TEvent).Name, @event.EventId);

        List<Func<DotflowEvent, CancellationToken, Task>> matching;
        lock (_handlersLock)
        {
            matching = _handlers
                .Where(h => h.EventType.IsAssignableFrom(typeof(TEvent)))
                .Select(h => h.Handler)
                .ToList();
        }

        foreach (var handler in matching)
        {
            try
            {
                await handler(@event, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in event handler for {EventType}", typeof(TEvent).Name);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler)
        where TEvent : DotflowEvent
    {
        Func<DotflowEvent, CancellationToken, Task> wrapped = (e, ct) =>
            e is TEvent typed ? handler(typed, ct) : Task.CompletedTask;

        lock (_handlersLock)
        {
            _handlers.Add((typeof(TEvent), wrapped));
        }

        return new Subscription(() =>
        {
            lock (_handlersLock)
            {
                _handlers.Remove((typeof(TEvent), wrapped));
            }
        });
    }

    private sealed class Subscription(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}
