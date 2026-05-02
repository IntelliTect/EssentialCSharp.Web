using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Services;

internal sealed partial class McpRateLimiterPolicy : IRateLimiterPolicy<string>
{
    internal const string PolicyName = "mcp";
    internal const int AuthenticatedTokenLimit = 45;
    internal const int AuthenticatedTokensPerPeriod = 1;
    internal static readonly TimeSpan AuthenticatedReplenishmentPeriod = TimeSpan.FromSeconds(2);
    internal const int AnonymousPermitLimit = 30;
    internal static readonly TimeSpan AnonymousWindow = TimeSpan.FromMinutes(1);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => OnRejectedAsync;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            string userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.User.Identity?.Name
                ?? "unknown-user";

            return RateLimitPartition.GetTokenBucketLimiter(
                partitionKey: $"mcp-user:{userId}",
                factory: _ => new TokenBucketRateLimiterOptions
                {
                    AutoReplenishment = true,
                    TokenLimit = AuthenticatedTokenLimit,
                    TokensPerPeriod = AuthenticatedTokensPerPeriod,
                    ReplenishmentPeriod = AuthenticatedReplenishmentPeriod,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
        }

        string partitionKey = $"mcp-ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip"}";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = AnonymousPermitLimit,
                Window = AnonymousWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        RateLimitingResponseHelpers.ApplyRetryAfterHeader(context.HttpContext.Response, context.Lease);
        await McpJsonRpcResponseWriter.WriteErrorAsync(
            context.HttpContext.Response,
            StatusCodes.Status429TooManyRequests,
            -32000,
            "Rate limit exceeded. Please wait before sending another request.",
            cancellationToken);

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<McpRateLimiterPolicy>>();
        LogMcpRateLimitExceeded(
            logger,
            context.HttpContext.Request.Path,
            context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            context.HttpContext.Connection.RemoteIpAddress);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP rate limit exceeded on {Path}. User: {User}, IP: {IpAddress}")]
    private static partial void LogMcpRateLimitExceeded(ILogger<McpRateLimiterPolicy> logger, PathString path, string user, System.Net.IPAddress? ipAddress);
}
