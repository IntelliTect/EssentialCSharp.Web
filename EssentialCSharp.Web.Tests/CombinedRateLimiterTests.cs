using System.Threading.RateLimiting;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Unit tests for <see cref="CombinedRateLimiter"/> — no HTTP, no factory.
/// AutoReplenishment is disabled so permits are consumed deterministically without timer callbacks.
/// </summary>
public class CombinedRateLimiterTests
{
    private static CombinedRateLimiter CreateLimiter(int perMinute, int perHour) =>
        new(
            new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 3,
                AutoReplenishment = false,
                QueueLimit = 0
            }),
            new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = perHour,
                Window = TimeSpan.FromHours(1),
                AutoReplenishment = false,
                QueueLimit = 0
            })
        );

    [Test]
    public async Task PerMinuteLimitIsEnforced_WhenPerHourIsSufficient()
    {
        using var limiter = CreateLimiter(perMinute: 3, perHour: 1000);

        for (int i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire();
            await Assert.That(lease.IsAcquired).IsTrue();
        }

        using var rejected = limiter.AttemptAcquire();
        await Assert.That(rejected.IsAcquired).IsFalse();
    }

    [Test]
    public async Task PerHourLimitIsEnforced_WhenPerMinuteIsSufficient()
    {
        using var limiter = CreateLimiter(perMinute: 1000, perHour: 3);

        for (int i = 0; i < 3; i++)
        {
            using var lease = limiter.AttemptAcquire();
            await Assert.That(lease.IsAcquired).IsTrue();
        }

        using var rejected = limiter.AttemptAcquire();
        await Assert.That(rejected.IsAcquired).IsFalse();
    }

    [Test]
    public async Task BothLimitsPass_AcquiredLeaseIsSuccessful()
    {
        using var limiter = CreateLimiter(perMinute: 5, perHour: 5);

        using var lease = limiter.AttemptAcquire();
        await Assert.That(lease.IsAcquired).IsTrue();
    }
}
