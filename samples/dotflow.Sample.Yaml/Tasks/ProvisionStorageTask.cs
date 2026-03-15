using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Yaml.Tasks;

public class ProvisionStorageTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        var userId = context.GetInput<string>("userId") ?? "unknown";
        context.Logger.LogInformation("Provisioning storage for user {UserId}...", userId);
        await Task.Delay(150, ct);
        context.Logger.LogInformation("Storage provisioned for user {UserId}", userId);
        context.SetOutput("storageBucket", $"bucket-{userId}");

        await context.Events.PublishAsync(new StorageProvisionedEvent
        {
            WorkflowRunId = context.WorkflowRunId,
            UserId = userId
        }, ct);
    }
}
