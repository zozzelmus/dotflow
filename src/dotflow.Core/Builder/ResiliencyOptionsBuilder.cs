using Dotflow.Configuration;

namespace Dotflow.Builder;

public class ResiliencyOptionsBuilder
{
    private RetryOptions? _retry;
    private CircuitBreakerOptions? _circuitBreaker;
    private TimeoutOptions? _timeout;

    public ResiliencyOptionsBuilder WithRetry(int maxAttempts, RetryStrategy strategy = RetryStrategy.ExponentialBackoff, TimeSpan? baseDelay = null)
    {
        _retry = new RetryOptions
        {
            MaxAttempts = maxAttempts,
            Strategy = strategy,
            BaseDelay = baseDelay ?? TimeSpan.FromSeconds(1)
        };
        return this;
    }

    public ResiliencyOptionsBuilder WithCircuitBreaker(int failureThreshold = 5, TimeSpan? breakDuration = null)
    {
        _circuitBreaker = new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            BreakDuration = breakDuration ?? TimeSpan.FromSeconds(10)
        };
        return this;
    }

    public ResiliencyOptionsBuilder WithTimeout(TimeSpan timeout)
    {
        _timeout = new TimeoutOptions { Timeout = timeout };
        return this;
    }

    internal ResiliencyOptions Build() => new()
    {
        Retry = _retry,
        CircuitBreaker = _circuitBreaker,
        Timeout = _timeout
    };
}
