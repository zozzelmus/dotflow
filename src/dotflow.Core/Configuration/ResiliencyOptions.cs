namespace Dotflow.Configuration;

public record ResiliencyOptions
{
    public RetryOptions? Retry { get; init; }
    public CircuitBreakerOptions? CircuitBreaker { get; init; }
    public TimeoutOptions? Timeout { get; init; }
}

public record RetryOptions
{
    public int MaxAttempts { get; init; } = 3;
    public RetryStrategy Strategy { get; init; } = RetryStrategy.ExponentialBackoff;
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);
    public double JitterFactor { get; init; } = 0.2;
}

public record CircuitBreakerOptions
{
    public int FailureThreshold { get; init; } = 5;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(10);
    public double FailureRatio { get; init; } = 0.5;
}

public record TimeoutOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);
}

public enum RetryStrategy
{
    Fixed,
    LinearBackoff,
    ExponentialBackoff
}
