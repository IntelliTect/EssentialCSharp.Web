using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Chat.Common.Models;

/// <summary>
/// Configuration options for retry logic when calling external services like Azure OpenAI.
/// </summary>
public sealed class EmbeddingRetryOptions
{
    /// <summary>
    /// Configuration section path in appsettings.json.
    /// </summary>
    public const string SectionPath = "AIOptions:EmbeddingRetry";

    /// <summary>
    /// Maximum number of retries for transient failures.
    /// Default is 5 retries (initial attempt + 5 retries = 6 total attempts).
    /// </summary>
    [Range(0, 20)]
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay in milliseconds before the first retry.
    /// Subsequent retries use exponential backoff: baseDelay * (backoffMultiplier ^ attemptNumber).
    /// Default is 1000ms (1 second).
    /// </summary>
    [Range(0, 600000)]
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum delay in milliseconds for exponential backoff before jitter.
    /// This caps retry delays to avoid overflow and unbounded waits.
    /// </summary>
    [Range(1, 600000)]
    public int MaxDelayMs { get; set; } = 60000;

    /// <summary>
    /// Maximum embedding request payload size sent per API call.
    /// The service may adaptively downshift below this value when throttled.
    /// </summary>
    [Range(1, 2048)]
    public int MaxEmbeddingBatchSize { get; set; } = 1024;

    /// <summary>
    /// Minimum delay between embedding API requests in milliseconds.
    /// This adds request pacing to reduce sustained rate-limit pressure.
    /// </summary>
    [Range(0, 600000)]
    public int MinInterRequestDelayMs { get; set; } = 250;

    /// <summary>
    /// Exponential backoff multiplier. Each retry delay is multiplied by this value.
    /// For example, with baseDelay=1000ms and multiplier=2.0:
    /// - 1st retry: 1000ms
    /// - 2nd retry: 2000ms
    /// - 3rd retry: 4000ms
    /// - 4th retry: 8000ms
    /// Default is 2.0 (double each time).
    /// </summary>
    [Range(1.0, 10.0)]
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum jitter fraction added to each retry delay to prevent thundering herd.
    /// Jitter is a random value in range [0, maxDelay * maxJitterFraction].
    /// For example, with maxJitterFraction=0.2 and delay=1000ms:
    /// actual delay will be between 1000ms and 1200ms.
    /// Default is 0.2 (20% jitter).
    /// </summary>
    [Range(0.0, 1.0)]
    public double MaxJitterFraction { get; set; } = 0.2;

    /// <summary>
    /// Validates that configuration values are reasonable.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if configuration is invalid.</exception>
    public void Validate()
    {
        if (MaxRetries < 0)
            throw new InvalidOperationException("MaxRetries must be non-negative.");

        if (BaseDelayMs < 0)
            throw new InvalidOperationException("BaseDelayMs must be non-negative.");

        if (MaxDelayMs <= 0)
            throw new InvalidOperationException("MaxDelayMs must be positive.");

        if (BaseDelayMs > MaxDelayMs)
            throw new InvalidOperationException("BaseDelayMs must be less than or equal to MaxDelayMs.");

        if (MaxEmbeddingBatchSize <= 0)
            throw new InvalidOperationException("MaxEmbeddingBatchSize must be positive.");

        if (MaxEmbeddingBatchSize > 2048)
            throw new InvalidOperationException("MaxEmbeddingBatchSize cannot exceed Azure embedding API limit (2048).");

        if (MinInterRequestDelayMs < 0)
            throw new InvalidOperationException("MinInterRequestDelayMs must be non-negative.");

        if (BackoffMultiplier < 1.0)
            throw new InvalidOperationException("BackoffMultiplier must be >= 1.0.");

        if (MaxJitterFraction < 0.0 || MaxJitterFraction > 1.0)
            throw new InvalidOperationException("MaxJitterFraction must be between 0.0 and 1.0.");
    }
}
