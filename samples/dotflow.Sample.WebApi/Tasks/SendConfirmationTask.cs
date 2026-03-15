using Dotflow.Abstractions;

namespace Dotflow.Sample.WebApi.Tasks;

public class SendConfirmationTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Sending confirmation in run {RunId}", context.WorkflowRunId);
        await Task.Delay(200, ct);
        context.Logger.LogInformation("Confirmation sent");
    }
}
