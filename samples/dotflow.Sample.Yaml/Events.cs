using Dotflow.Models;

namespace Dotflow.Sample.Yaml;

public record StorageProvisionedEvent : DotflowEvent
{
    public required string UserId { get; init; }
}
