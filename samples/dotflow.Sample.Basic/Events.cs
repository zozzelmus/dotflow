using Dotflow.Models;

namespace Dotflow.Sample.Basic;

public record OrderValidatedEvent : DotflowEvent
{
    public required string OrderId { get; init; }
}

public record PaymentProcessedEvent : DotflowEvent
{
    public required string OrderId { get; init; }
}
