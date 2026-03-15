using Dotflow.Builder;
using Dotflow.Validation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dotflow.Engine;

internal sealed class DotflowHostedService : IHostedService
{
    private readonly IReadOnlyList<WorkflowDefinition> _workflows;
    private readonly WorkflowValidator _validator;
    private readonly ILogger<DotflowHostedService> _logger;

    public DotflowHostedService(
        IReadOnlyList<WorkflowDefinition> workflows,
        WorkflowValidator validator,
        ILogger<DotflowHostedService> logger)
    {
        _workflows = workflows;
        _validator = validator;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Validating {Count} workflow definition(s)...", _workflows.Count);
        _validator.Validate();
        _logger.LogInformation("Dotflow startup validation passed.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
