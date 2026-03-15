using Dotflow.Abstractions;
using Microsoft.Extensions.Logging;

namespace Dotflow.Sample.Yaml.Tasks;

public class SendWelcomeEmailTask : DotflowTask
{
    public override async Task ExecuteAsync(ITaskContext context, CancellationToken ct = default)
    {
        var userId = context.GetInput<string>("userId") ?? "unknown";
        context.Logger.LogInformation("Sending welcome email to user {UserId}...", userId);
        await Task.Delay(80, ct);
        context.Logger.LogInformation("Welcome email sent to user {UserId}", userId);
        context.SetOutput("welcomeEmailSent", true);
    }
}
