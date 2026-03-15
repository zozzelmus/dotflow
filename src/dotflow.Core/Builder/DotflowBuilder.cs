using Dotflow.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dotflow.Builder;

public class DotflowBuilder
{
    private readonly IServiceCollection _services;
    internal DotflowOptions Options { get; } = new();

    internal DotflowBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public DotflowBuilder AddWorkflow(string id, Action<WorkflowBuilder> configure)
    {
        var builder = new WorkflowBuilder(id);
        configure(builder);
        Options.Workflows.Add(builder.Build());
        return this;
    }

    public DotflowBuilder ConfigureResiliency(Action<ResiliencyOptionsBuilder> configure)
    {
        var builder = new ResiliencyOptionsBuilder();
        configure(builder);
        Options.Resiliency = builder.Build();
        return this;
    }
}
