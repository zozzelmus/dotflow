using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Basic.Tasks;

public class ValidateOrderTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Validating order...");
        await Task.Delay(100, ct);

        var orderId = context.GetInput<string>("orderId") ?? "unknown";
        context.Logger.LogInformation("Order {OrderId} validated", orderId);
        context.SetOutput("isValid", true);
        context.SetOutput("orderId", orderId);

        await context.Events.PublishAsync(new OrderValidatedEvent
        {
            WorkflowRunId = context.WorkflowRunId,
            OrderId = orderId
        }, ct);
    }
}
