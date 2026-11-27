using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Middleware;

public sealed class ReferralMiddleware
{
    private readonly RequestDelegate _Next;

    public ReferralMiddleware(RequestDelegate next)
    {
        _Next = next;
    }

    public async Task InvokeAsync(HttpContext context, IReferralService referralService, UserManager<EssentialCSharpWebUser> userManager)
    {
        // Retrieve current referral Id for processing
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(context.Request.QueryString.Value ?? string.Empty);
        string? referralId = query["rid"];
        if (string.IsNullOrWhiteSpace(referralId) || !IsTrackableRequest(context))
        {
            await _Next(context);
            return;
        }

        if (context.User is { Identity.IsAuthenticated: true } claimsUser)
        {
            referralService.TrackReferralAsync(referralId, claimsUser);
        }
        else
        {
            referralService.TrackReferralAsync(referralId, null);
        }

        await _Next(context);
    }

    /// <summary>
    /// Only page views count: GET requests to endpoints that are rate limited. Endpoints that opt out of rate
    /// limiting (health checks, /version, /.well-known) are not page views, and counting them would let anyone add
    /// to a referral count as fast as they can send requests.
    /// </summary>
    private static bool IsTrackableRequest(HttpContext context) =>
        HttpMethods.IsGet(context.Request.Method)
        && context.GetEndpoint()?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is null;
}
