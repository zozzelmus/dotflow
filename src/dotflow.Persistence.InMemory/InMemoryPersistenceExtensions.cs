using Dotflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dotflow.Persistence.InMemory;

public static class InMemoryPersistenceExtensions
{
    /// <summary>
    /// Registers the in-memory pipeline store. Dev/test only — single-instance, not multi-container-safe.
    /// </summary>
    public static IServiceCollection UseInMemoryStore(this IServiceCollection services)
    {
        services.AddSingleton<IPipelineStore, InMemoryPipelineStore>();
        return services;
    }
}
