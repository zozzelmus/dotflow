using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Basic.Tasks;

public class ChargeCardTask : DotflowTask
{
    private static int _attemptCount;

    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        var attempt = Interlocked.Increment(ref _attemptCount);
        context.Logger.LogInformation("Charging card (attempt {Attempt})...", attempt);

        // Simulate a transient failure on first attempt to demonstrate retry
        if (attempt == 1)
        {
            context.Logger.LogWarning("Payment gateway temporarily unavailable, will retry...");
            throw new InvalidOperationException("Payment gateway timeout (simulated)");
        }

        await Task.Delay(200, ct);
        context.Logger.LogInformation("Card charged successfully");
        context.SetOutput("chargeId", $"ch_{Ulid.NewUlid()}");

        await context.Events.PublishAsync(new PaymentProcessedEvent
        {
            WorkflowRunId = context.WorkflowRunId,
            OrderId = context.GetInput<string>("orderId") ?? ""
        }, ct);
    }
}
