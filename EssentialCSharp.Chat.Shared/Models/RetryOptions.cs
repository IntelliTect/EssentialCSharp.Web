namespace EssentialCSharp.Chat.Common.Models;

/// <summary>
/// Configuration options for retry logic when calling external services like Azure OpenAI.
/// </summary>
public class RetryOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "EmbeddingRetry";

    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// Default is 5 attempts (initial attempt + 4 retries).
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay in milliseconds before the first retry.
    /// Subsequent retries use exponential backoff: baseDelay * (backoffMultiplier ^ attemptNumber).
    /// Default is 1000ms (1 second).
    /// </summary>
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Exponential backoff multiplier. Each retry delay is multiplied by this value.
    /// For example, with baseDelay=1000ms and multiplier=2.0:
    /// - 1st retry: 1000ms
    /// - 2nd retry: 2000ms
    /// - 3rd retry: 4000ms
    /// - 4th retry: 8000ms
    /// Default is 2.0 (double each time).
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum jitter fraction added to each retry delay to prevent thundering herd.
    /// Jitter is a random value in range [0, maxDelay * maxJitterFraction].
    /// For example, with maxJitterFraction=0.2 and delay=1000ms:
    /// actual delay will be between 1000ms and 1200ms.
    /// Default is 0.2 (20% jitter).
    /// </summary>
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

        if (BackoffMultiplier < 1.0)
            throw new InvalidOperationException("BackoffMultiplier must be >= 1.0.");

        if (MaxJitterFraction < 0.0 || MaxJitterFraction > 1.0)
            throw new InvalidOperationException("MaxJitterFraction must be between 0.0 and 1.0.");
    }
}
