using Dotflow.Events;
using Dotflow.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dotflow.Core.Tests;

public class InternalEventBusTests
{
    private record TestEvent : DotflowEvent
    {
        public string? Message { get; init; }
    }

    private record OtherEvent : DotflowEvent;

    [Fact]
    public async Task PublishAsync_DeliiversToSubscriber()
    {
        var bus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var received = new List<TestEvent>();

        using var sub = bus.Subscribe<TestEvent>((e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new TestEvent { Message = "hello" });

        Assert.Single(received);
        Assert.Equal("hello", received[0].Message);
    }

    [Fact]
    public async Task Subscribe_DoesNotReceiveOtherEvents()
    {
        var bus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var received = new List<TestEvent>();

        using var sub = bus.Subscribe<TestEvent>((e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        await bus.PublishAsync(new OtherEvent());

        Assert.Empty(received);
    }

    [Fact]
    public async Task Dispose_RemovesSubscription()
    {
        var bus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var received = new List<TestEvent>();

        var sub = bus.Subscribe<TestEvent>((e, _) =>
        {
            received.Add(e);
            return Task.CompletedTask;
        });

        sub.Dispose();
        await bus.PublishAsync(new TestEvent { Message = "after dispose" });

        Assert.Empty(received);
    }

    [Fact]
    public async Task MultipleSubscribers_BothReceiveEvent()
    {
        var bus = new InternalEventBus(NullLogger<InternalEventBus>.Instance);
        var count = 0;

        using var sub1 = bus.Subscribe<TestEvent>((_, _) => { Interlocked.Increment(ref count); return Task.CompletedTask; });
        using var sub2 = bus.Subscribe<TestEvent>((_, _) => { Interlocked.Increment(ref count); return Task.CompletedTask; });

        await bus.PublishAsync(new TestEvent());

        Assert.Equal(2, count);
    }
}
