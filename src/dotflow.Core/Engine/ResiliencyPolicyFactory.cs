using Dotflow.Configuration;
using Polly;

namespace Dotflow.Engine;

internal static class ResiliencyPolicyFactory
{
    internal static ResiliencePipeline Build(ResiliencyOptions? options)
    {
        if (options is null)
            return ResiliencePipeline.Empty;

        var builder = new ResiliencePipelineBuilder();

        if (options.Timeout is not null)
        {
            builder.AddTimeout(options.Timeout.Timeout);
        }

        if (options.CircuitBreaker is not null)
        {
            var cb = options.CircuitBreaker;
            builder.AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                FailureRatio = cb.FailureRatio,
                SamplingDuration = cb.SamplingDuration,
                MinimumThroughput = cb.FailureThreshold,
                BreakDuration = cb.BreakDuration
            });
        }

        if (options.Retry is not null)
        {
            var r = options.Retry;
            builder.AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = r.MaxAttempts,
                Delay = r.BaseDelay,
                MaxDelay = r.MaxDelay,
                BackoffType = r.Strategy switch
                {
                    RetryStrategy.Fixed => DelayBackoffType.Constant,
                    RetryStrategy.LinearBackoff => DelayBackoffType.Linear,
                    _ => DelayBackoffType.Exponential
                },
                UseJitter = r.JitterFactor > 0
            });
        }

        return builder.Build();
    }
}
