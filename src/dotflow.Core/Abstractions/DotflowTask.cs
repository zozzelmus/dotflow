namespace Dotflow.Abstractions;

/// <summary>
/// Atomic unit of work. Subclass this to implement your task logic.
///
/// NOTE: This type is named <c>DotflowTask</c> to avoid shadowing <c>System.Threading.Tasks.Task</c>.
/// It is exposed under the <c>Dotflow</c> namespace as <c>Dotflow.Task</c>. If you use both in the same
/// file, alias one: <c>using BCLTask = System.Threading.Tasks.Task;</c>
/// </summary>
public abstract class DotflowTask
{
    public virtual string Name => GetType().Name;

    public abstract System.Threading.Tasks.Task ExecuteAsync(ITaskContext context, CancellationToken ct = default);
}
