using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Middleware;

public sealed class ReferralMiddleware
{
    private readonly RequestDelegate _Next;
    private readonly ILogger<ReferralMiddleware> _Logger;

    public ReferralMiddleware(RequestDelegate next, ILogger<ReferralMiddleware> logger)
    {
        _Next = next;
        _Logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IReferralService referralService, UserManager<EssentialCSharpWebUser> userManager)
    {
        // Retrieve current referral Id for processing
        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(context.Request.QueryString.Value!);
        string? referralId = query["rid"];
        if (string.IsNullOrWhiteSpace(referralId))
        {
            await _Next(context);
            return;
        }

        try
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
        catch (Exception ex)
        {
            _Logger.LogError(ex, "Failed to track referral ID {ReferralId}", referralId);
        }

        await _Next(context);
    }
}
