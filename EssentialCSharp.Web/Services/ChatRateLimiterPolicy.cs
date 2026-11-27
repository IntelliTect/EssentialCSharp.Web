using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

/// <summary>Configuration section <c>ChatThrottling</c>.</summary>
public sealed class ChatThrottlingOptions
{
    public const string SectionName = "ChatThrottling";

    /// <summary>Length of the longer chat budget window, in minutes.</summary>
    [Range(1, 24 * 60)]
    public int ThrottlingWindowMinutes { get; set; } = 60;

    /// <summary>Messages one user (or one IP, when anonymous) may send per <see cref="ThrottlingWindowMinutes"/>.</summary>
    [Range(1, 10_000)]
    public int MaxMessagesPerWindow { get; set; } = 50;
}

/// <summary>
/// Rate limiting for the chat API: a 15-per-minute burst limit chained with the configured
/// <see cref="ChatThrottlingOptions.MaxMessagesPerWindow"/> per <see cref="ChatThrottlingOptions.ThrottlingWindowMinutes"/>,
/// both partitioned by user ID (or IP when anonymous).
/// </summary>
internal sealed class ChatRateLimiterPolicy(IOptions<ChatThrottlingOptions> options) : IRateLimiterPolicy<string>
{
    internal const string PolicyName = "ChatEndpoint";
    internal const int PerMinuteLimit = 15;

    // Null defers to the global OnRejected handler registered in Program.cs, which writes the JSON error.
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        string partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? $"chat-user:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-user"}"
            : $"chat-ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip"}";

        ChatThrottlingOptions throttling = options.Value;
        return RateLimitPartition.Get(partitionKey, _ => new CombinedRateLimiter(
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = PerMinuteLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = throttling.MaxMessagesPerWindow,
                Window = TimeSpan.FromMinutes(throttling.ThrottlingWindowMinutes),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            })));
    }
}
