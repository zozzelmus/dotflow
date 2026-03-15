using System.Reflection;
using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Configuration;
using Dotflow.Engine;
using Dotflow.Events;
using Dotflow.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dotflow;

public static class DotflowServiceCollectionExtensions
{
    public static IServiceCollection AddDotflow(
        this IServiceCollection services,
        Action<DotflowBuilder> configure)
    {
        var builder = new DotflowBuilder(services);
        configure(builder);

        var options = builder.Options;
        var workflows = options.Workflows.AsReadOnly();

        services.AddSingleton<IReadOnlyList<WorkflowDefinition>>(workflows);
        services.AddSingleton<IEventBus, InternalEventBus>();
        services.AddSingleton<WorkflowValidator>(sp =>
            new WorkflowValidator(workflows, sp));
        services.AddSingleton<TaskExecutor>(sp =>
            new TaskExecutor(
                sp,
                sp.GetRequiredService<ILogger<TaskExecutor>>(),
                options.Resiliency));
        services.AddSingleton<PhaseExecutor>(sp =>
            new PhaseExecutor(
                sp.GetRequiredService<TaskExecutor>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<ILogger<PhaseExecutor>>()));
        services.AddSingleton<IWorkflowEngine, WorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IReadOnlyList<WorkflowDefinition>>(),
                sp.GetRequiredService<IPipelineStore>(),
                sp.GetRequiredService<IEventBus>(),
                sp.GetRequiredService<PhaseExecutor>(),
                sp.GetRequiredService<ILoggerFactory>()));
        services.AddHostedService<DotflowHostedService>(sp =>
            new DotflowHostedService(
                sp.GetRequiredService<IReadOnlyList<WorkflowDefinition>>(),
                sp.GetRequiredService<WorkflowValidator>(),
                sp.GetRequiredService<ILogger<DotflowHostedService>>()));

        return services;
    }

    public static IServiceCollection AddDotflowTasksFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        var taskTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(DotflowTask).IsAssignableFrom(t));

        foreach (var type in taskTypes)
            services.AddTransient(type);

        return services;
    }
}
