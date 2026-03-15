using Dotflow.Abstractions;

namespace Dotflow.Sample.WebApi.Tasks;

public class ValidateOrderTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Validating order in run {RunId}", context.WorkflowRunId);
        await Task.Delay(300, ct);
        context.Logger.LogInformation("Order validated");
    }
}
