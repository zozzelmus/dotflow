using Dotflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dotflow.Persistence.PostgreSQL;

public static class PostgreSQLPersistenceExtensions
{
    public static IServiceCollection UsePostgreSQL(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IPipelineStore>(_ => new PostgreSQLPipelineStore(connectionString));
        return services;
    }
}
