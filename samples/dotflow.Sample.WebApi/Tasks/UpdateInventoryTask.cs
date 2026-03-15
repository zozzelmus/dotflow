using Dotflow.Abstractions;

namespace Dotflow.Sample.WebApi.Tasks;

public class UpdateInventoryTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("Updating inventory in run {RunId}", context.WorkflowRunId);
        await Task.Delay(400, ct);
        context.Logger.LogInformation("Inventory updated");
    }
}
