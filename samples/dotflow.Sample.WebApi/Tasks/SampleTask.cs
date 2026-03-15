using Dotflow.Abstractions;

namespace Dotflow.Sample.WebApi.Tasks;

public class SampleTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        context.Logger.LogInformation("SampleTask running in run {RunId}", context.WorkflowRunId);
        await Task.Delay(500, ct);
        context.Logger.LogInformation("SampleTask done");
    }
}
