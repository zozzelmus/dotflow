using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Dotflow.Dashboard;

public static class DashboardExtensions
{
    public static IApplicationBuilder UseDotflowDashboard(
        this IApplicationBuilder app,
        string pathPrefix = "/dotflow",
        Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions { PathPrefix = pathPrefix };
        configure?.Invoke(options);

        app.UseMiddleware<DashboardMiddleware>(options);
        return app;
    }

    public static IServiceCollection AddDotflowDashboard(this IServiceCollection services)
    {
        return services;
    }
}
