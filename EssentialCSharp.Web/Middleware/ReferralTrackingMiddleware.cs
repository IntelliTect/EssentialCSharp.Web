using System.Security.Claims;
using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;

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
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(context.Request.QueryString.Value!);
        string? referralId = query["rid"];
        if (context.User is { Identity.IsAuthenticated: true } claimsUser)
        {
            TrackReferralAsync(referralService, referralId, claimsUser);
        }
        else
        {
            TrackReferralAsync(referralService, referralId, null);
        }

        await _Next(context);

        static void TrackReferralAsync(IReferralService referralService, string? referralId, ClaimsPrincipal? claimsUser)
        {
            if (!string.IsNullOrWhiteSpace(referralId))
            {
                referralService.TrackReferralAsync(referralId, claimsUser);
            }
        }
    }
}
