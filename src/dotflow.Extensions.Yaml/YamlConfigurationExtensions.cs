using Dotflow.Builder;
using Dotflow.Configuration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dotflow.Extensions.Yaml;

public static class YamlConfigurationExtensions
{
    /// <summary>
    /// Load workflow definitions from a YAML string into a DotflowBuilder.
    /// The YAML must match the DotflowYamlConfig schema.
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
            builder.AddWorkflow(wf.Id ?? throw new InvalidOperationException("Workflow 'id' is required."), wb =>
            {
                if (wf.Name is not null) wb.WithName(wf.Name);
                foreach (var phase in wf.Phases ?? [])
                {
                    wb.AddPhase(phase.Name ?? throw new InvalidOperationException("Phase 'name' is required."), pb =>
                    {
                        if (phase.Trigger?.Type == "Immediate")
                            pb.TriggeredImmediately();
                    });
                }
            });
        }

        return builder;
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
