using Microsoft.AspNetCore.Http;

namespace Dotflow.Dashboard;

public record DashboardOptions
{
    public string PathPrefix { get; set; } = "/dotflow";
    public bool RequireAuthentication { get; set; } = false;
    public Func<HttpContext, bool>? AuthorizationFilter { get; set; }
    public string Title { get; set; } = "dotflow";
}
