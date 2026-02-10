# Azure OpenAI Resilience Configuration

## Overview

This document describes the resilience and retry mechanisms implemented for Azure OpenAI API calls in the EssentialCSharp.Web application. These mechanisms handle transient failures, rate limiting (HTTP 429), and other recoverable errors.

## Implementation

### Retry Policy

The application uses Microsoft.Extensions.Http.Resilience to provide automatic retry capabilities for all Azure OpenAI HTTP client calls. This includes:

- **Embedding Generation** (via `IEmbeddingGenerator`)
- **Chat Completions** (via `IChatCompletionService`)
- **Vector Store Operations**

### Configuration Details

The resilience handler is configured in `ServiceCollectionExtensions.ConfigureAzureOpenAIResilience()` with the following settings:

#### Retry Strategy
- **Max Retry Attempts**: 5
- **Initial Delay**: 2 seconds
- **Backoff Type**: Exponential with jitter
- **Handles**:
  - HTTP 429 (Too Many Requests / Rate Limit Exceeded)
  - HTTP 408 (Request Timeout)
  - HTTP 5xx (Server Errors)
  - Network failures and transient errors

#### Retry-After Header Support
The standard resilience handler automatically respects the `Retry-After` header sent by Azure OpenAI when rate limits are hit. This ensures:
- The application waits the exact duration specified by Azure
- No unnecessary retries that would continue to hit rate limits
- Efficient use of rate limit quotas

#### Circuit Breaker
- **Sampling Duration**: 30 seconds
- **Break Duration**: 15 seconds
- **Failure Ratio**: 20% (breaks if 20% of requests fail)

This prevents overwhelming the Azure OpenAI service during prolonged outages or severe rate limiting.

#### Timeouts
- **Attempt Timeout**: 30 seconds per individual request
- **Total Request Timeout**: 3 minutes for all retry attempts combined

## How It Works

### Rate Limit Scenario (HTTP 429)

When Azure OpenAI returns an HTTP 429 error:

1. The resilience handler catches the error
2. Checks the `Retry-After` header (e.g., "retry after 4 seconds")
3. Waits for the specified duration (with jitter to prevent thundering herd)
4. Retries the request automatically
5. Repeats up to 5 times with exponential backoff
6. If all retries fail, the exception is propagated to the caller

### Example Flow

```
Request 1: Embedding Generation
  → HTTP 429 (Retry-After: 4 seconds)
  → Wait 4 seconds + jitter
  → Retry
Request 2: Embedding Generation
  → HTTP 429 (Retry-After: 8 seconds)
  → Wait 8 seconds + jitter
  → Retry
Request 3: Embedding Generation
  → HTTP 200 ✓
```

## Benefits

### For Rate Limiting
- Automatic handling of Azure OpenAI quota limits
- Respects server-specified retry delays
- Prevents quota waste from premature retries
- Exponential backoff prevents retry storms

### For Reliability
- Handles transient network failures
- Recovers from temporary service outages
- Circuit breaker prevents cascading failures
- Configurable timeouts prevent infinite waits

## Usage

The resilience configuration is applied automatically when using the Chat application or any application that ONLY uses Azure OpenAI services:

```csharp
// In applications that ONLY use Azure OpenAI (like EssentialCSharp.Chat)
services.AddAzureOpenAIServices(configuration);
```

For applications with multiple HTTP clients (e.g., the Web application that also uses hCaptcha and Mailjet):

```csharp
// Option 1: Disable automatic resilience and configure per-client
services.AddAzureOpenAIServices(configuration, configureResilience: false);

// Then configure resilience for specific clients as needed
services.AddHttpClient("MyAzureOpenAIClient")
    .AddStandardResilienceHandler(/* custom options */);

// Option 2: Let the default resilience apply to all clients
// This is usually fine as the resilience policies are reasonable for most HTTP APIs
services.AddAzureOpenAIServices(configuration);
```

No additional code changes are required in application logic - all retry and error handling is transparent.

## Monitoring

The resilience handlers emit telemetry through:
- **Application Insights** (when configured)
- **OpenTelemetry** (standard metrics)
- **Console logging** (for development)

Key metrics include:
- Retry attempts
- Circuit breaker state changes
- Request durations
- Failure rates

## Best Practices

### For Development
- Monitor retry counts in logs
- Test with rate limiting scenarios
- Verify Retry-After header handling

### For Production
- Set up alerts for high retry rates
- Monitor circuit breaker trips
- Track rate limit quota usage
- Consider increasing Azure OpenAI quota if needed

## Troubleshooting

### Excessive Retries
If you see many retries:
- Check Azure OpenAI quota limits
- Review request parallelism (currently set to MaxDegreeOfParallelism = 5)
- Consider requesting quota increase from Azure

### Circuit Breaker Trips
If the circuit breaker frequently opens:
- Review Azure OpenAI service health
- Check for deployment issues
- Verify network connectivity
- Consider increasing the failure ratio threshold

### Timeouts
If requests timeout despite retries:
- Check individual attempt timeout (30s)
- Review total timeout (3 minutes)
- Verify Azure OpenAI service performance
- Consider increasing timeout values for batch operations

## Related Documentation

- [Microsoft.Extensions.Http.Resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
- [Azure OpenAI Quota Management](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/how-to/quota)
- [Polly Resilience Library](https://www.pollydocs.org/)
- [HTTP Retry Pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/retry)

## Version History

- **v1.0** (2026-02-10): Initial implementation with standard resilience handler
  - Added exponential backoff retry strategy
  - Configured circuit breaker and timeouts
  - Automatic Retry-After header support
