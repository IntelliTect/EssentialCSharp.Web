using System.Threading.RateLimiting;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Unit tests for the link checker bypass in <see cref="ContentRateLimiterPolicy"/> — no HTTP, no factory.
/// </summary>
public class ContentRateLimiterPolicyLinkCheckerTests
{
    private const string ConfiguredKey = "test-link-checker-key";
    private const int AnonymousPerMinute = 10;

    [Test]
    public async Task MatchingKey_IsNotRateLimited()
    {
        using RateLimiter limiter = CreateLimiter(ConfiguredKey, providedKey: ConfiguredKey);

        await Assert.That(CountAcquired(limiter, AnonymousPerMinute * 5)).IsEqualTo(AnonymousPerMinute * 5);
    }

    [Test]
    [Arguments("wrong-key")]
    [Arguments(null)]
    public async Task MissingOrWrongKey_IsStillRateLimited(string? providedKey)
    {
        using RateLimiter limiter = CreateLimiter(ConfiguredKey, providedKey);

        await Assert.That(CountAcquired(limiter, AnonymousPerMinute * 5)).IsEqualTo(AnonymousPerMinute);
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public async Task NoConfiguredKey_NeverExempts(string? configuredKey)
    {
        // An unset key must not match an empty/absent header.
        using RateLimiter limiter = CreateLimiter(configuredKey, providedKey: configuredKey);

        await Assert.That(CountAcquired(limiter, AnonymousPerMinute * 5)).IsEqualTo(AnonymousPerMinute);
    }

    private static RateLimiter CreateLimiter(string? configuredKey, string? providedKey)
    {
        ServiceCollection services = new();
        services.AddSingleton<IOptions<ContentRateLimitOptions>>(
            Options.Create(new ContentRateLimitOptions { LinkCheckerKey = configuredKey }));

        DefaultHttpContext httpContext = new()
        {
            RequestServices = services.BuildServiceProvider()
        };
        if (providedKey is not null)
        {
            httpContext.Request.Headers[ContentRateLimiterPolicy.LinkCheckerKeyHeader] = providedKey;
        }

        RateLimitPartition<string> partition = new ContentRateLimiterPolicy().GetPartition(httpContext);
        return partition.Factory(partition.PartitionKey);
    }

    private static int CountAcquired(RateLimiter limiter, int attempts)
    {
        int acquired = 0;
        for (int i = 0; i < attempts; i++)
        {
            using RateLimitLease lease = limiter.AttemptAcquire();
            if (lease.IsAcquired) acquired++;
        }
        return acquired;
    }
}
