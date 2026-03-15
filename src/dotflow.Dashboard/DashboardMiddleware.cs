using Dotflow.Abstractions;
using Dotflow.Builder;
using Dotflow.Models;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;

namespace Dotflow.Dashboard;

public sealed class DashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DashboardOptions _options;
    private readonly IPipelineStore _store;
    private readonly IWorkflowEngine _engine;
    private readonly IReadOnlyList<WorkflowDefinition> _workflows;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public DashboardMiddleware(
        RequestDelegate next,
        DashboardOptions options,
        IPipelineStore store,
        IWorkflowEngine engine,
        IReadOnlyList<WorkflowDefinition> workflows)
    {
        _next = next;
        _options = options;
        _store = store;
        _engine = engine;
        _workflows = workflows;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        var prefix = _options.PathPrefix.TrimEnd('/');

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        if (_options.RequireAuthentication && !ctx.User.Identity?.IsAuthenticated == true)
        {
            ctx.Response.StatusCode = 401;
            return;
        }

        if (_options.AuthorizationFilter is not null && !_options.AuthorizationFilter(ctx))
        {
            ctx.Response.StatusCode = 403;
            return;
        }

        var subPath = path[prefix.Length..].TrimStart('/');
        var method = ctx.Request.Method;

        try
        {
            if (subPath == "api/stats" && method == "GET")
            {
                await HandleStatsAsync(ctx);
            }
            else if (subPath.StartsWith("api/runs/") && subPath.EndsWith("/cancel") && method == "POST")
            {
                var runId = subPath["api/runs/".Length..^"/cancel".Length];
                await HandleCancelAsync(ctx, runId);
            }
            else if (subPath.StartsWith("api/runs/") && !subPath.Contains("/events") && method == "GET")
            {
                var runId = subPath["api/runs/".Length..];
                await HandleGetRunAsync(ctx, runId);
            }
            else if (subPath == "api/runs" && method == "GET")
            {
                await HandleListRunsAsync(ctx);
            }
            else if (subPath.StartsWith("api/workflows/") && subPath.EndsWith("/trigger") && method == "POST")
            {
                var workflowId = subPath["api/workflows/".Length..^"/trigger".Length];
                await HandleTriggerAsync(ctx, workflowId);
            }
            else if (subPath == "api/workflows" && method == "GET")
            {
                await HandleListWorkflowsAsync(ctx);
            }
            else if (subPath.StartsWith("api/workflows/") && !subPath.EndsWith("/trigger") && method == "GET")
            {
                var workflowId = subPath["api/workflows/".Length..];
                await HandleGetWorkflowAsync(ctx, workflowId);
            }
            else
            {
                await ServePageAsync(ctx, subPath);
            }
        }
        catch (Exception ex)
        {
            ctx.Response.StatusCode = 500;
            await WriteJsonAsync(ctx, new { error = ex.Message });
        }
    }

    private async Task HandleStatsAsync(HttpContext ctx)
    {
        var stats = await _store.GetStatsAsync(ctx.RequestAborted);
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(BuildStatsHtml(stats));
        }
        else
        {
            await WriteJsonAsync(ctx, stats);
        }
    }

    private async Task HandleListRunsAsync(HttpContext ctx)
    {
        var qs = ctx.Request.Query;
        var query = new RunQuery
        {
            WorkflowId = qs["workflowId"],
            Status = qs["status"].FirstOrDefault() is { } s ? Enum.Parse<RunStatus>(s) : null,
            Page = int.TryParse(qs["page"], out var p) ? p : 1,
            PageSize = int.TryParse(qs["pageSize"], out var ps) ? ps : 20
        };
        var result = await _store.ListRunsAsync(query, ctx.RequestAborted);
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            var prefix = _options.PathPrefix.TrimEnd('/');
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(BuildRunsTableHtml(prefix, result));
        }
        else
        {
            await WriteJsonAsync(ctx, result);
        }
    }

    private async Task HandleGetRunAsync(HttpContext ctx, string runId)
    {
        var run = await _store.GetRunAsync(runId, ctx.RequestAborted);
        if (run is null)
        {
            ctx.Response.StatusCode = 404;
            return;
        }
        if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            var prefix = _options.PathPrefix.TrimEnd('/');
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(BuildRunDetailHtml(prefix, run));
        }
        else
        {
            await WriteJsonAsync(ctx, run);
        }
    }

    private async Task HandleCancelAsync(HttpContext ctx, string runId)
    {
        await _engine.CancelAsync(runId, ctx.RequestAborted);
        ctx.Response.StatusCode = 202;
    }

    private async Task HandleTriggerAsync(HttpContext ctx, string workflowId)
    {
        Dictionary<string, object?>? input = null;
        if (ctx.Request.ContentLength > 0)
        {
            input = await JsonSerializer.DeserializeAsync<Dictionary<string, object?>>(
                ctx.Request.Body, JsonOpts, ctx.RequestAborted);
        }

        var run = await _engine.TriggerAsync(workflowId, input, ctx.RequestAborted);

        if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            var prefix = _options.PathPrefix.TrimEnd('/');
            ctx.Response.Headers["HX-Redirect"] = $"{prefix}/runs/{run.Id}";
            ctx.Response.StatusCode = 200;
        }
        else
        {
            ctx.Response.StatusCode = 202;
            await WriteJsonAsync(ctx, new { runId = run.Id });
        }
    }

    private async Task HandleListWorkflowsAsync(HttpContext ctx)
    {
        var prefix = _options.PathPrefix.TrimEnd('/');
        var ct = ctx.RequestAborted;

        var summaries = await Task.WhenAll(_workflows.Select(async wf =>
        {
            var recent = await _store.ListRunsAsync(
                new RunQuery { WorkflowId = wf.Id, Page = 1, PageSize = 1 }, ct);
            var last = recent.Items.FirstOrDefault();
            return new WorkflowSummary(wf.Id, wf.Name ?? wf.Id, wf.Phases.Count,
                last?.Status.ToString(), last?.Id);
        }));

        if (ctx.Request.Headers.ContainsKey("HX-Request"))
        {
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await ctx.Response.WriteAsync(BuildWorkflowCardsHtml(prefix, summaries));
        }
        else
        {
            await WriteJsonAsync(ctx, summaries);
        }
    }

    private Task HandleGetWorkflowAsync(HttpContext ctx, string workflowId)
    {
        var wf = _workflows.FirstOrDefault(w => w.Id == workflowId);
        if (wf is null) { ctx.Response.StatusCode = 404; return Task.CompletedTask; }

        return WriteJsonAsync(ctx, new
        {
            id = wf.Id,
            name = wf.Name ?? wf.Id,
            phases = wf.Phases.Select(p => new
            {
                name = p.Name,
                trigger = DescribeTrigger(p.Trigger),
                continueOnFailure = p.ContinueOnFailure,
                tasks = p.Tasks.Select(slot => slot switch
                {
                    TaskSlot.SingleTask s => (object)new { kind = "single", name = ShortTypeName(s.TaskType.Name) },
                    TaskSlot.ConcurrentGroup g => new { kind = "concurrent", tasks = g.TaskTypes.Select(t => ShortTypeName(t.Name)).ToList() },
                    _ => new { kind = "unknown" }
                }).ToList()
            }).ToList()
        });
    }

    private Task ServePageAsync(HttpContext ctx, string subPath)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        var title = _options.Title;
        var prefix = _options.PathPrefix;
        var html = subPath switch
        {
            "" or "index" => BuildOverviewPage(title, prefix),
            "workflows" => BuildWorkflowsPage(title, prefix),
            "runs" => BuildRunsPage(title, prefix),
            _ when subPath.StartsWith("runs/") => BuildRunDetailPage(title, prefix, subPath["runs/".Length..]),
            _ when subPath.StartsWith("workflows/") => BuildWorkflowDetailPage(title, prefix, subPath["workflows/".Length..]),
            _ => "<html><body><h1>404 - Not Found</h1></body></html>"
        };
        return ctx.Response.WriteAsync(html);
    }

    private static string ShortTypeName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot >= 0 ? fullName[(dot + 1)..] : fullName;
    }

    private static string DescribeTrigger(PhaseTrigger? trigger) => trigger switch
    {
        null or PhaseTrigger.Immediate => "Immediate",
        PhaseTrigger.OnEvent e => $"On: {ShortTypeName(e.EventType)}",
        { } t when t.GetType().IsGenericType =>
            $"On: {ShortTypeName(t.GetType().GetProperty("EventType")?.GetValue(t) as string ?? t.GetType().Name)}",
        _ => "Unknown"
    };

    private static string StatusBadge(RunStatus status)
    {
        var cls = status switch
        {
            RunStatus.Succeeded => "badge-succeeded",
            RunStatus.Failed => "badge-failed",
            RunStatus.Running => "badge-running",
            RunStatus.Pending => "badge-pending",
            RunStatus.Cancelled => "badge-cancelled",
            _ => "badge-pending"
        };
        return $"<span class=\"badge {cls}\">{status}</span>";
    }

    private static string FormatDuration(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null || end is null) return "&#8212;";
        return $"{(end.Value - start.Value).TotalSeconds:0.##}s";
    }

    private static string BuildStatsHtml(RunStats stats)
    {
        var avgDuration = stats.AverageDuration.HasValue
            ? $"{stats.AverageDuration.Value.TotalSeconds:0.##}s"
            : "&#8212;";
        var successRate = stats.TotalRuns > 0
            ? $"{stats.SuccessRate:0.#}%"
            : "&#8212;";

        return
            "<div class=\"stat-grid\">" +
            StatCard("Total", stats.TotalRuns.ToString()) +
            StatCard("Succeeded", stats.SucceededRuns.ToString()) +
            StatCard("Failed", stats.FailedRuns.ToString()) +
            StatCard("Running", stats.RunningRuns.ToString()) +
            StatCard("Success Rate", successRate) +
            StatCard("Avg Duration", avgDuration) +
            "</div>";

        static string StatCard(string label, string value) =>
            $"<div class=\"stat-card\"><div class=\"stat-value\">{value}</div><div class=\"stat-label\">{label}</div></div>";
    }

    private static string BuildRunsTableHtml(string prefix, PagedResult<WorkflowRun> result)
    {
        if (result.Items.Count == 0)
            return "<p class=\"muted\">No runs yet.</p>";

        var sb = new StringBuilder();
        sb.Append("<table class=\"run-table\"><thead><tr>");
        sb.Append("<th>Run ID</th><th>Workflow</th><th>Status</th><th>Created</th><th>Duration</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var run in result.Items)
        {
            var shortId = run.Id.Length > 12 ? run.Id[..12] : run.Id;
            var created = run.CreatedAt.LocalDateTime.ToString("MMM d, HH:mm:ss");
            var duration = FormatDuration(run.StartedAt, run.FinishedAt);
            sb.Append("<tr>");
            sb.Append($"<td><a href=\"{prefix}/runs/{run.Id}\">{shortId}&hellip;</a></td>");
            sb.Append($"<td>{run.WorkflowId}</td>");
            sb.Append($"<td>{StatusBadge(run.Status)}</td>");
            sb.Append($"<td>{created}</td>");
            sb.Append($"<td>{duration}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string BuildRunDetailHtml(string prefix, WorkflowRun run)
    {
        var sb = new StringBuilder();

        sb.Append("<div style=\"display:flex;align-items:center;gap:.75rem;margin-bottom:1rem;flex-wrap:wrap\">");
        sb.Append(StatusBadge(run.Status));
        sb.Append($"<span>Workflow: <a href=\"{prefix}/workflows/{run.WorkflowId}\">{run.WorkflowId}</a></span>");
        sb.Append($"<span style=\"color:var(--muted)\">Created: {run.CreatedAt.LocalDateTime:MMM d, HH:mm:ss}</span>");
        sb.Append($"<span style=\"color:var(--muted)\">Duration: {FormatDuration(run.StartedAt, run.FinishedAt)}</span>");
        sb.Append("</div>");

        foreach (var phase in run.Phases)
        {
            sb.Append("<div class=\"phase-section\">");
            sb.Append("<div class=\"phase-section-header\">");
            sb.Append($"<span>{phase.PhaseName}</span>");
            sb.Append(StatusBadge(phase.Status));
            sb.Append("</div>");

            foreach (var task in phase.Tasks)
            {
                sb.Append("<div class=\"task-row\">");
                sb.Append(StatusBadge(task.Status));
                sb.Append($"<span>{task.TaskName}</span>");
                if (task.AttemptCount > 1)
                    sb.Append($"<span style=\"color:var(--muted);font-size:.8rem\">{task.AttemptCount} attempts</span>");
                if (!string.IsNullOrEmpty(task.ErrorMessage))
                    sb.Append($"<span style=\"color:#991b1b;font-size:.8rem\">{task.ErrorMessage}</span>");
                sb.Append("</div>");
            }

            sb.Append("</div>");
        }

        if (run.Status is RunStatus.Pending or RunStatus.Running)
        {
            sb.Append($"<div style=\"margin-top:1.5rem\">");
            sb.Append($"<form hx-post=\"{prefix}/api/runs/{run.Id}/cancel\" hx-confirm=\"Cancel this run?\">");
            sb.Append("<button type=\"submit\" class=\"btn btn-danger\">Cancel Run</button>");
            sb.Append("</form></div>");
        }

        return sb.ToString();
    }

    private static string BuildPhaseCard(PhaseDefinition phase)
    {
        var sb = new StringBuilder();
        var triggerClass = phase.Trigger is PhaseTrigger.Immediate or null ? "badge-immediate" : "badge-event";
        sb.Append($"<div class=\"phase-card\"><div class=\"phase-header\">");
        sb.Append($"<span class=\"phase-name\">{phase.Name}</span>");
        sb.Append($"<span class=\"badge {triggerClass}\">{DescribeTrigger(phase.Trigger)}</span>");
        if (phase.ContinueOnFailure)
            sb.Append("<span class=\"badge badge-warn\">ContinueOnFailure</span>");
        sb.Append("</div><ul class=\"task-list\">");
        foreach (var slot in phase.Tasks)
        {
            switch (slot)
            {
                case TaskSlot.SingleTask s:
                    sb.Append($"<li class=\"task-item\">{ShortTypeName(s.TaskType.Name)}</li>");
                    break;
                case TaskSlot.ConcurrentGroup g:
                    sb.Append($"<li class=\"task-item task-concurrent\">&#8214; ");
                    sb.Append(string.Join(" + ", g.TaskTypes.Select(t => ShortTypeName(t.Name))));
                    sb.Append("</li>");
                    break;
            }
        }
        sb.Append("</ul></div>");
        return sb.ToString();
    }

    private static string BuildPipelineDiagram(string prefix, WorkflowDefinition wf)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"pipeline-flow\">");
        for (int i = 0; i < wf.Phases.Count; i++)
        {
            if (i > 0)
            {
                var label = DescribeTrigger(wf.Phases[i].Trigger);
                sb.Append($"<div class=\"pipeline-arrow\"><span class=\"trigger-label\">{label}</span><span>&#8594;</span></div>");
            }
            sb.Append(BuildPhaseCard(wf.Phases[i]));
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildWorkflowCardsHtml(string prefix, IEnumerable<WorkflowSummary> summaries)
    {
        var sb = new StringBuilder();
        sb.Append("<div class=\"wf-grid\">");
        foreach (var wf in summaries)
        {
            var statusBadge = wf.LastRunStatus is null
                ? "<span class=\"badge badge-never\">Never run</span>"
                : $"<span class=\"badge badge-{wf.LastRunStatus.ToLowerInvariant()}\">{wf.LastRunStatus}</span>";
            sb.Append("<div class=\"wf-card\">");
            sb.Append($"<div class=\"wf-card-header\"><span class=\"wf-name\">{wf.Name}</span>{statusBadge}</div>");
            sb.Append($"<div class=\"wf-card-meta\"><span>ID: <code>{wf.Id}</code></span><span>{wf.PhaseCount} phase{(wf.PhaseCount == 1 ? "" : "s")}</span></div>");
            sb.Append($"<div class=\"wf-card-actions\">");
            sb.Append($"<a href=\"{prefix}/workflows/{wf.Id}\" class=\"btn btn-sm\">View</a> ");
            sb.Append($"<form hx-post=\"{prefix}/api/workflows/{wf.Id}/trigger\" hx-confirm=\"Trigger {wf.Name}?\" style=\"display:inline\">");
            sb.Append("<button class=\"btn btn-sm btn-primary\">&#9654; Trigger</button></form>");
            sb.Append("</div></div>");
        }
        sb.Append("</div>");
        return sb.ToString();
    }

    private string BuildWorkflowDetailPage(string title, string prefix, string workflowId)
    {
        prefix = prefix.TrimEnd('/');
        var wf = _workflows.FirstOrDefault(w => w.Id == workflowId);
        if (wf is null)
            return PageShell(title, prefix,
                $"<h2>Workflow not found</h2><p><a href=\"{prefix}/workflows\">Back to workflows</a></p>", "workflows");

        var wfName = wf.Name ?? wf.Id;
        var body =
            $"<div class=\"breadcrumb\"><a href=\"{prefix}/workflows\">Workflows</a> &rsaquo; {wfName}</div>" +
            $"<div class=\"detail-header\"><div><h2 style=\"margin:0\">{wfName}</h2>" +
            $"<code class=\"wf-id\">{wf.Id}</code></div>" +
            $"<form hx-post=\"{prefix}/api/workflows/{workflowId}/trigger\" hx-confirm=\"Trigger {wfName}?\">" +
            "<button class=\"btn btn-primary\">&#9654; Trigger Workflow</button></form></div>" +
            BuildPipelineDiagram(prefix, wf) +
            "<hr/><h3>Recent Runs</h3>" +
            $"<div hx-get=\"{prefix}/api/runs?workflowId={workflowId}\" hx-trigger=\"load, every 5s\" hx-swap=\"innerHTML\">Loading...</div>";

        return PageShell($"{title} \u2014 {wfName}", prefix, body, "workflows");
    }

    private string BuildWorkflowsPage(string title, string prefix) =>
        PageShell(title, prefix.TrimEnd('/'),
            $"<h2>Workflows <span style=\"font-size:.8rem;color:#6b7280;font-weight:400\">({_workflows.Count} registered)</span></h2>" +
            $"<div hx-get=\"{prefix.TrimEnd('/')}/api/workflows\" hx-trigger=\"load, every 15s\" hx-swap=\"innerHTML\">" +
            "<p>Loading workflows...</p></div>", "workflows");

    private string BuildOverviewPage(string title, string prefix)
    {
        prefix = prefix.TrimEnd('/');
        var wfLinks = _workflows.Count == 0
            ? "<em>No workflows registered.</em>"
            : string.Join(" &bull; ", _workflows.Select(
                wf => $"<a href=\"{prefix}/workflows/{wf.Id}\">{wf.Name ?? wf.Id}</a>"));
        return PageShell(title, prefix,
            $"<div hx-get=\"{prefix}/api/stats\" hx-trigger=\"load, every 10s\" hx-swap=\"innerHTML\">Loading stats...</div>" +
            $"<p class=\"wf-jump\">Workflows: {wfLinks}</p>", "");
    }

    private static string BuildRunsPage(string title, string prefix) =>
        PageShell(title, prefix.TrimEnd('/'),
            $"<h2>Run History</h2><div hx-get=\"{prefix.TrimEnd('/')}/api/runs\" hx-trigger=\"load, every 5s\" hx-swap=\"innerHTML\">Loading...</div>",
            "runs");

    private static string BuildRunDetailPage(string title, string prefix, string runId)
    {
        prefix = prefix.TrimEnd('/');
        return PageShell($"{title} \u2014 Run {runId}", prefix,
            $"<h2>Run: {runId}</h2>" +
            $"<div hx-get=\"{prefix}/api/runs/{runId}\" hx-trigger=\"load, every 3s\" hx-swap=\"innerHTML\">Loading...</div>",
            "runs");
    }

    private static string Nav(string prefix, string section)
    {
        static string Link(string href, string label, string current, string key)
        {
            var cls = current == key ? " class=\"active\"" : "";
            return $"<a href=\"{href}\"{cls}>{label}</a>";
        }
        return "<nav>" +
            Link(prefix, "Overview", section, "") +
            Link($"{prefix}/workflows", "Workflows", section, "workflows") +
            Link($"{prefix}/runs", "Runs", section, "runs") +
            "</nav>";
    }

    private static string PageShell(string title, string prefix, string body, string section) =>
        "<!DOCTYPE html><html lang=\"en\"><head>" +
        "<meta charset=\"utf-8\"/><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"/>" +
        $"<title>{title}</title>" +
        "<link rel=\"icon\" href=\"data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 32 32'><rect width='32' height='32' rx='6' fill='%233b82f6'/><text x='16' y='22' font-size='18' font-family='system-ui' fill='white' text-anchor='middle'>df</text></svg>\"/>" +
        "<script src=\"https://unpkg.com/htmx.org@1.9.12\"></script>" +
        "<style>" +
        ":root{--bg:#f9fafb;--card:#fff;--border:#e5e7eb;--text:#111827;--muted:#6b7280;--primary:#3b82f6;--primary-dark:#2563eb}" +
        "body{font-family:system-ui,sans-serif;background:var(--bg);color:var(--text);max-width:1200px;margin:2rem auto;padding:0 1rem}" +
        "h1{margin-bottom:.25rem}" +
        "nav{display:flex;gap:1rem;padding:.75rem 0;border-bottom:1px solid var(--border);margin-bottom:1rem}" +
        "nav a{color:var(--primary);text-decoration:none;font-weight:500}" +
        "nav a:hover{text-decoration:underline}" +
        "hr{border:none;border-top:1px solid var(--border);margin:1.5rem 0}" +
        ".breadcrumb{color:var(--muted);margin-bottom:1rem;font-size:.9rem}" +
        ".breadcrumb a{color:var(--primary);text-decoration:none}" +
        ".detail-header{display:flex;align-items:center;justify-content:space-between;margin-bottom:1.5rem;flex-wrap:wrap;gap:1rem}" +
        ".pipeline-flow{display:flex;align-items:flex-start;overflow-x:auto;padding:.5rem 0;gap:.25rem}" +
        ".phase-card{background:var(--card);border:1px solid var(--border);border-radius:.5rem;padding:1rem;min-width:180px;max-width:260px}" +
        ".phase-header{display:flex;flex-wrap:wrap;align-items:center;gap:.35rem;margin-bottom:.5rem}" +
        ".phase-name{font-weight:600;font-size:.95rem}" +
        ".pipeline-arrow{display:flex;flex-direction:column;align-items:center;justify-content:center;padding:0 .5rem;color:var(--muted);white-space:nowrap;min-width:60px;padding-top:1rem}" +
        ".trigger-label{font-size:.7rem;color:var(--muted);margin-bottom:.2rem;text-align:center}" +
        ".task-list{list-style:none;padding:0;margin:.25rem 0 0 0;display:flex;flex-direction:column;gap:.25rem}" +
        ".task-item{font-size:.85rem;background:#f3f4f6;border-radius:.25rem;padding:.2rem .5rem}" +
        ".task-concurrent{background:#ede9fe;color:#5b21b6}" +
        ".badge{display:inline-block;padding:.15rem .5rem;border-radius:999px;font-size:.75rem;font-weight:500;white-space:nowrap}" +
        ".badge-immediate{background:#dbeafe;color:#1e40af}" +
        ".badge-event{background:#fef3c7;color:#92400e}" +
        ".badge-warn{background:#fee2e2;color:#991b1b}" +
        ".badge-never{background:#f3f4f6;color:#6b7280}" +
        ".badge-succeeded{background:#d1fae5;color:#065f46}" +
        ".badge-failed{background:#fee2e2;color:#991b1b}" +
        ".badge-running{background:#dbeafe;color:#1e40af}" +
        ".badge-pending{background:#f3f4f6;color:#6b7280}" +
        ".badge-cancelled{background:#f3f4f6;color:#6b7280}" +
        ".wf-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(280px,1fr));gap:1rem;margin-top:1rem}" +
        ".wf-card{background:var(--card);border:1px solid var(--border);border-radius:.5rem;padding:1.25rem;display:flex;flex-direction:column;gap:.75rem}" +
        ".wf-card-header{display:flex;align-items:center;justify-content:space-between;gap:.5rem}" +
        ".wf-name{font-weight:600;font-size:1rem}" +
        ".wf-card-meta{display:flex;gap:1rem;font-size:.85rem;color:var(--muted)}" +
        ".wf-card-actions{display:flex;gap:.5rem;align-items:center}" +
        ".btn{display:inline-block;padding:.4rem .85rem;border-radius:.375rem;border:1px solid var(--border);background:var(--card);color:var(--text);cursor:pointer;font-size:.875rem;text-decoration:none;font-family:inherit}" +
        ".btn:hover{background:#f3f4f6}" +
        ".btn-sm{padding:.25rem .6rem;font-size:.8rem}" +
        ".btn-primary{background:var(--primary);color:#fff;border-color:var(--primary)}" +
        ".btn-primary:hover{background:var(--primary-dark);border-color:var(--primary-dark)}" +
        ".wf-jump{font-size:.9rem;color:var(--muted);margin-top:.5rem}" +
        ".wf-jump a{color:var(--primary);text-decoration:none}" +
        ".wf-jump a:hover{text-decoration:underline}" +
        "code.wf-id{font-size:.8rem;background:#f3f4f6;padding:.1rem .4rem;border-radius:.25rem;color:var(--muted)}" +
        ".stat-grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:1rem;margin-bottom:1.5rem}" +
        ".stat-card{background:var(--card);border:1px solid var(--border);border-radius:.5rem;padding:1rem}" +
        ".stat-value{font-size:1.75rem;font-weight:700;line-height:1.1}" +
        ".stat-label{font-size:.8rem;color:var(--muted);margin-top:.25rem}" +
        ".run-table{width:100%;border-collapse:collapse;font-size:.875rem}" +
        ".run-table th{text-align:left;padding:.5rem .75rem;border-bottom:2px solid var(--border);font-weight:600;color:var(--muted);font-size:.8rem}" +
        ".run-table td{padding:.5rem .75rem;border-bottom:1px solid var(--border);vertical-align:middle}" +
        ".run-table tr:last-child td{border-bottom:none}" +
        ".run-table tr:hover td{background:#f9fafb}" +
        ".phase-section{margin-top:1.25rem}" +
        ".phase-section-header{display:flex;align-items:center;gap:.5rem;margin-bottom:.5rem;font-weight:600}" +
        ".task-row{display:flex;align-items:center;gap:.5rem;padding:.3rem .5rem;border-radius:.25rem;font-size:.85rem}" +
        ".task-row:nth-child(odd){background:#f9fafb}" +
        ".muted{color:var(--muted);font-size:.875rem}" +
        "nav a.active{color:var(--text);font-weight:600;pointer-events:none}" +
        ".btn-danger{background:#ef4444;color:#fff;border-color:#ef4444}" +
        ".btn-danger:hover{background:#dc2626;border-color:#dc2626}" +
        "</style>" +
        "</head><body>" +
        "<h1>dotflow</h1>" +
        Nav(prefix, section) + "<hr/>" +
        body +
        "</body></html>";

    private static Task WriteJsonAsync(HttpContext ctx, object data)
    {
        ctx.Response.ContentType = "application/json";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(data, JsonOpts));
    }

    private record WorkflowSummary(string Id, string Name, int PhaseCount, string? LastRunStatus, string? LastRunId);
}
