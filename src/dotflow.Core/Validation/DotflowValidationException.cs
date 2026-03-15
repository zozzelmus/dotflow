namespace Dotflow.Validation;

public class DotflowValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public DotflowValidationException(IReadOnlyList<string> errors)
        : base($"Dotflow validation failed with {errors.Count} error(s):\n" + string.Join("\n", errors.Select(e => $"  - {e}")))
    {
        Errors = errors;
    }
}
