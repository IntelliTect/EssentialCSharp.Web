using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Rate limiting policy for book content pages (HomeController.Index).
/// Authenticated users get higher limits (20/min, 60/hr) partitioned by user ID.
/// Anonymous users get stricter limits (10/min, 150/hr) partitioned by IP.
/// </summary>
internal sealed class ContentRateLimiterPolicy : IRateLimiterPolicy<string>
{
    private const int AnonymousPerMinute = 10;
    private const int AnonymousPerHour = 20;
    private const int AuthenticatedPerMinute = 20;
    private const int AuthenticatedPerHour = 60;

    // Null defers to the global OnRejected handler registered in Program.cs
    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => null;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        bool isAuthenticated = httpContext.User.Identity?.IsAuthenticated == true;

        // Use stable user ID (GUID) for authenticated users so the bucket survives
        // username changes and doesn't conflate login/logout with scraping.
        string partitionKey = isAuthenticated
            ? $"user:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? httpContext.User.Identity!.Name ?? "unknown"}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip"}";

        int perMinuteLimit = isAuthenticated ? AuthenticatedPerMinute : AnonymousPerMinute;
        int perHourLimit = isAuthenticated ? AuthenticatedPerHour : AnonymousPerHour;

        return RateLimitPartition.Get(partitionKey, _ => new CombinedRateLimiter(
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = perMinuteLimit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = perHourLimit,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            })
        ));
    }
}

/// <summary>
/// A <see cref="RateLimiter"/> that acquires permits from two inner limiters in sequence.
/// Both must grant a permit for the request to proceed. If the second limiter rejects,
/// the first limiter's permit is returned so no permit is consumed.
/// </summary>
internal sealed class CombinedRateLimiter(RateLimiter perMinute, RateLimiter perHour) : RateLimiter
{
    public override TimeSpan? IdleDuration
    {
        get
        {
            TimeSpan? a = perMinute.IdleDuration;
            TimeSpan? b = perHour.IdleDuration;
            if (a is null || b is null) return null;
            return a < b ? a : b;
        }
    }

    public override RateLimiterStatistics? GetStatistics() => perMinute.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        RateLimitLease minLease = perMinute.AttemptAcquire(permitCount);
        if (!minLease.IsAcquired) return minLease;

        RateLimitLease hourLease = perHour.AttemptAcquire(permitCount);
        if (!hourLease.IsAcquired)
        {
            minLease.Dispose(); // return the per-minute permit we just took
            return hourLease;
        }

        return new CombinedRateLimitLease(minLease, hourLease);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        RateLimitLease minLease = await perMinute.AcquireAsync(permitCount, cancellationToken);
        if (!minLease.IsAcquired) return minLease;

        RateLimitLease hourLease = await perHour.AcquireAsync(permitCount, cancellationToken);
        if (!hourLease.IsAcquired)
        {
            minLease.Dispose();
            return hourLease;
        }

        return new CombinedRateLimitLease(minLease, hourLease);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { perMinute.Dispose(); perHour.Dispose(); }
        base.Dispose(disposing);
    }

    protected override ValueTask DisposeAsyncCore()
    {
        perMinute.Dispose();
        perHour.Dispose();
        return base.DisposeAsyncCore();
    }
}

/// <summary>
/// A lease that holds two inner leases and disposes both on release,
/// ensuring permits are returned to both underlying limiters.
/// </summary>
internal sealed class CombinedRateLimitLease(RateLimitLease a, RateLimitLease b) : RateLimitLease
{
    public override bool IsAcquired => true;

    public override IEnumerable<string> MetadataNames => [.. a.MetadataNames, .. b.MetadataNames];

    public override bool TryGetMetadata(string metadataName, out object? metadata)
    {
        if (a.TryGetMetadata(metadataName, out metadata)) return true;
        return b.TryGetMetadata(metadataName, out metadata);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { a.Dispose(); b.Dispose(); }
        base.Dispose(disposing);
    }
}
