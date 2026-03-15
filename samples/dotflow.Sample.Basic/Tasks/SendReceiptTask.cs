using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Basic.Tasks;

public class SendReceiptTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Sending receipt email...");
        await Task.Delay(50, ct);
        context.Logger.LogInformation("Receipt sent");
    }
}
