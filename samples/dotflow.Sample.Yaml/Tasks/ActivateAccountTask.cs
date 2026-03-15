using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Yaml.Tasks;

public class ActivateAccountTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        var userId = context.GetInput<string>("userId") ?? "unknown";
        var bucket = context.GetInput<string>("storageBucket") ?? "unknown";
        context.Logger.LogInformation("Activating account for user {UserId} (storage: {Bucket})...", userId, bucket);
        await Task.Delay(100, ct);
        context.Logger.LogInformation("Account activated for user {UserId}", userId);
        context.SetOutput("accountActive", true);
    }
}
