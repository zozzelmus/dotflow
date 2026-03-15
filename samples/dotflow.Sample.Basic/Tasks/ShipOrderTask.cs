using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Basic.Tasks;

public class ShipOrderTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Creating shipment...");
        await Task.Delay(150, ct);
        context.Logger.LogInformation("Order shipped. Tracking: {Tracking}", $"TRK-{Ulid.NewUlid()}");
    }
}
