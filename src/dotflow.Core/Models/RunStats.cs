namespace Dotflow.Models;

public class RunStats
{
    public int TotalRuns { get; init; }
    public int SucceededRuns { get; init; }
    public int FailedRuns { get; init; }
    public int RunningRuns { get; init; }
    public double SuccessRate => TotalRuns == 0 ? 0 : (double)SucceededRuns / TotalRuns * 100;
    public TimeSpan? AverageDuration { get; init; }
}
