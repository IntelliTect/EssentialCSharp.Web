using System.Security.Claims;
using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Http.Extensions;
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
        string? userReferralId;

        if (context.User is { Identity.IsAuthenticated: true } claimsUser)
        {
            if (!string.IsNullOrWhiteSpace(referralId))
            {
                await TrackReferralAsync(referralService, referralId, claimsUser);
            }

            // Add the referralId to the request context if it exists on a user
            EssentialCSharpWebUser? user = await userManager.GetUserAsync(claimsUser);
            if (user is not null)
            {
                userReferralId = await referralService.GetReferralIdAsync(user.Id);

                if (!string.IsNullOrWhiteSpace(userReferralId) && (string.IsNullOrWhiteSpace(query["rid"]) || query["rid"] != userReferralId))
                {
                    query.Remove("rid");
                    query.Add("rid", userReferralId);
                    var builder = new UriBuilder(context.Request.GetEncodedUrl())
                    {
                        Query = query.ToString()
                    };
                    context.Response.Redirect(builder.ToString());
                    return;
                }
            }
        }
        else
        {

            if (!string.IsNullOrWhiteSpace(referralId))
            {
                await TrackReferralAsync(referralService, referralId, null);
                query.Remove("rid");
                var builder = new UriBuilder(context.Request.GetEncodedUrl())
                {
                    Query = query.ToString()
                };
                context.Response.Redirect(builder.ToString());
                return;
            }
        }

        await _Next(context);

        static async Task TrackReferralAsync(IReferralService referralService, string? referralId, ClaimsPrincipal? claimsUser)
        {
            if (!string.IsNullOrWhiteSpace(referralId))
            {
                await referralService.TrackReferralAsync(referralId, claimsUser);
            }
        }
    }
}
