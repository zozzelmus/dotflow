using Dotflow.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Dotflow.Validation;

public sealed class WorkflowValidator
{
    private readonly IReadOnlyList<WorkflowDefinition> _workflows;
    private readonly IServiceProvider _services;

    public WorkflowValidator(IReadOnlyList<WorkflowDefinition> workflows, IServiceProvider services)
    {
        _workflows = workflows;
        _services = services;
    }

    public void Validate()
    {
        var errors = new List<string>();

        var workflowIds = new HashSet<string>();
        foreach (var workflow in _workflows)
        {
            if (!workflowIds.Add(workflow.Id))
                errors.Add($"Duplicate workflow ID '{workflow.Id}'.");

            ValidateWorkflow(workflow, errors);
        }

        if (errors.Count > 0)
            throw new DotflowValidationException(errors);
    }

    private void ValidateWorkflow(WorkflowDefinition workflow, List<string> errors)
    {
        foreach (var phase in workflow.Phases)
        {
            var prefix = $"Phase '{phase.Name}' in workflow '{workflow.Id}'";

            if (phase.Trigger is null)
            {
                errors.Add($"{prefix} has no trigger configured. Call TriggeredImmediately() or TriggeredOn<T>().");
            }

            if (phase.Tasks.Count == 0)
            {
                errors.Add($"{prefix} has no tasks. A phase must have at least one task.");
            }

            ValidateTaskRegistrations(phase, prefix, errors);
        }

        ValidateEventTriggers(workflow, errors);
    }

    private void ValidateTaskRegistrations(PhaseDefinition phase, string prefix, List<string> errors)
    {
        var allTaskTypes = phase.Tasks
            .SelectMany(slot => slot switch
            {
                TaskSlot.SingleTask s => [s.TaskType],
                TaskSlot.ConcurrentGroup g => g.TaskTypes,
                _ => []
            });

        foreach (var taskType in allTaskTypes)
        {
            try
            {
                var svc = _services.GetService(taskType);
                if (svc is null)
                    errors.Add($"{prefix}: task '{taskType.Name}' is not registered in DI. Call services.AddTransient<{taskType.Name}>().");
            }
            catch
            {
                errors.Add($"{prefix}: task '{taskType.Name}' could not be resolved from DI.");
            }
        }
    }

    private static void ValidateEventTriggers(WorkflowDefinition workflow, List<string> errors)
    {
        var hasImmediatePhase = workflow.Phases.Any(p => p.Trigger is PhaseTrigger.Immediate);
        if (!hasImmediatePhase)
            errors.Add($"Workflow '{workflow.Id}' has no Immediate phase. At least one phase must use TriggeredImmediately() to start the workflow.");

        var eventToPhases = new Dictionary<string, List<string>>();
        foreach (var phase in workflow.Phases)
        {
            if (phase.Trigger is not { } trigger) continue;
            var eventType = GetEventType(trigger);
            if (eventType is null) continue;

            if (!eventToPhases.TryGetValue(eventType, out var list))
                eventToPhases[eventType] = list = [];
            list.Add(phase.Name);
        }

        foreach (var (eventType, phases) in eventToPhases)
        {
            if (phases.Count > 1)
                errors.Add($"Workflow '{workflow.Id}' has multiple phases triggered by event '{eventType}': {string.Join(", ", phases)}. Each event type should trigger at most one phase.");
        }
    }

    private static string? GetEventType(PhaseTrigger trigger)
    {
        if (trigger is PhaseTrigger.OnEvent oe) return oe.EventType;
        var t = trigger.GetType();
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(OnEvent<>))
        {
            var eventArg = t.GetGenericArguments()[0];
            return eventArg.FullName ?? eventArg.Name;
        }
        return null;
    }
}
