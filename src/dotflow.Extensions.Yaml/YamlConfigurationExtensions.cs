using Dotflow.Builder;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dotflow.Extensions.Yaml;

public static class YamlConfigurationExtensions
{
    /// <summary>
    /// Load workflow definitions from a YAML string.
    /// Task and event types must be fully qualified (e.g. "MyApp.Tasks.ValidateOrderTask, MyApp").
    /// </summary>
    public static DotflowBuilder LoadFromYaml(this DotflowBuilder builder, string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var config = deserializer.Deserialize<DotflowYamlConfig>(yaml)
            ?? throw new InvalidOperationException("Failed to deserialize YAML configuration.");

        foreach (var wf in config.Workflows ?? [])
        {
            var workflowId = wf.Id ?? throw new InvalidOperationException("Workflow 'id' is required.");

            builder.AddWorkflow(workflowId, wb =>
            {
                if (wf.Name is not null) wb.WithName(wf.Name);

                foreach (var phase in wf.Phases ?? [])
                {
                    var phaseName = phase.Name ?? throw new InvalidOperationException($"A phase in workflow '{workflowId}' is missing 'name'.");

                    wb.AddPhase(phaseName, pb =>
                    {
                        ConfigureTrigger(pb, phase.Trigger, workflowId, phaseName);

                        foreach (var task in phase.Tasks ?? [])
                        {
                            var typeName = task.Type ?? throw new InvalidOperationException(
                                $"A task in phase '{phaseName}' of workflow '{workflowId}' is missing 'type'.");

                            var taskType = Type.GetType(typeName)
                                ?? throw new InvalidOperationException(
                                    $"Task type '{typeName}' in phase '{phaseName}' of workflow '{workflowId}' could not be resolved. " +
                                    "Use a fully qualified type name including assembly (e.g. 'MyApp.Tasks.ValidateOrderTask, MyApp').");

                            pb.AddTask(taskType);
                        }
                    });
                }
            });
        }

        return builder;
    }

    private static void ConfigureTrigger(PhaseBuilder pb, TriggerYamlConfig? trigger, string workflowId, string phaseName)
    {
        if (trigger is null || trigger.Type == "Immediate")
        {
            pb.TriggeredImmediately();
            return;
        }

        if (trigger.Type == "OnEvent")
        {
            var eventTypeName = trigger.EventType ?? throw new InvalidOperationException(
                $"Phase '{phaseName}' in workflow '{workflowId}' has trigger type 'OnEvent' but is missing 'eventType'.");

            var eventType = Type.GetType(eventTypeName)
                ?? throw new InvalidOperationException(
                    $"Event type '{eventTypeName}' in phase '{phaseName}' of workflow '{workflowId}' could not be resolved. " +
                    "Use a fully qualified type name including assembly (e.g. 'MyApp.Events.OrderValidatedEvent, MyApp').");

            pb.TriggeredOn(eventType);
            return;
        }

        throw new InvalidOperationException(
            $"Unknown trigger type '{trigger.Type}' in phase '{phaseName}' of workflow '{workflowId}'. Valid values: Immediate, OnEvent.");
    }
}

public class DotflowYamlConfig
{
    public List<WorkflowYamlConfig>? Workflows { get; set; }
}

public class WorkflowYamlConfig
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public List<PhaseYamlConfig>? Phases { get; set; }
}

public class PhaseYamlConfig
{
    public string? Name { get; set; }
    public TriggerYamlConfig? Trigger { get; set; }
    public List<TaskYamlConfig>? Tasks { get; set; }
}

public class TriggerYamlConfig
{
    public string? Type { get; set; }
    public string? EventType { get; set; }
}

public class TaskYamlConfig
{
    public string? Type { get; set; }
}
