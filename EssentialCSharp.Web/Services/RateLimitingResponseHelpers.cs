using System.Globalization;
using System.Threading.RateLimiting;

namespace EssentialCSharp.Web.Services;

internal static class RateLimitingResponseHelpers
{
    public static int? ApplyRetryAfterHeader(HttpResponse response, RateLimitLease lease)
    {
        if (!lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            return null;
        }

        int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return retryAfterSeconds;
    }
}
