using Dotflow.Abstractions;

namespace Dotflow.Sample.WebApi.Tasks;

public class ProcessPaymentTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Processing payment in run {RunId}", context.WorkflowRunId);
        await Task.Delay(600, ct);
        context.Logger.LogInformation("Payment processed");
    }
}
