using System.Security.Claims;
using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;

namespace EssentialCSharp.Web.Middleware;

public sealed class ReferralMiddleware
{
    // A visitor counts once per referral link per window, however many pages they load with it.
    internal static readonly TimeSpan DedupeWindow = TimeSpan.FromHours(24);

    private readonly RequestDelegate _Next;

    public ReferralMiddleware(RequestDelegate next)
    {
        _Next = next;
    }

    public async Task InvokeAsync(HttpContext context, IReferralService referralService, UserManager<EssentialCSharpWebUser> userManager, IMemoryCache memoryCache)
    {
        // Retrieve current referral Id for processing
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(context.Request.QueryString.Value ?? string.Empty);
        string? referralId = query["rid"];
        if (string.IsNullOrWhiteSpace(referralId) || !IsTrackableRequest(context))
        {
            await _Next(context);
            return;
        }

        if (TryClaimVisit(memoryCache, referralId, GetVisitorKey(context)))
        {
            if (context.User is { Identity.IsAuthenticated: true } claimsUser)
            {
                referralService.TrackReferralAsync(referralId, claimsUser);
            }
            else
            {
                referralService.TrackReferralAsync(referralId, null);
            }
        }

        await _Next(context);
    }

    /// <summary>
    /// Only page views count. Endpoints that opt out of rate limiting (health checks, /version, /.well-known)
    /// would otherwise let anyone add to a referral count as fast as they can send requests.
    /// </summary>
    private static bool IsTrackableRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.GetEndpoint()?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null;

    private static string GetVisitorKey(HttpContext context) =>
        context.User.Identity?.IsAuthenticated == true
            ? $"user:{context.User.FindFirstValue(ClaimTypes.NameIdentifier)}"
            : $"ip:{context.Connection.RemoteIpAddress}";

    private static bool TryClaimVisit(IMemoryCache memoryCache, string referralId, string visitorKey)
    {
        // Referral IDs are matched case-insensitively by the database, so dedupe them the same way.
        string cacheKey = $"referral-visit:{referralId.ToUpperInvariant()}:{visitorKey}";
        if (memoryCache.TryGetValue(cacheKey, out _))
        {
            return false;
        }

        memoryCache.Set(cacheKey, true, DedupeWindow);
        return true;
    }
}
