using System.Security.Claims;
using System.Web;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Middleware;

public sealed class ReferralMiddleware
{
    private readonly RequestDelegate _Next;
    private readonly ILogger<ReferralMiddleware> _logger;

    public ReferralMiddleware(RequestDelegate next, ILogger<ReferralMiddleware> logger)
    {
        _Next = next;
        _logger = logger;
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
        
        // Track the referral, but don't let exceptions prevent the page from loading
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
            // Log the exception but continue processing the request
            // The referral tracking failure should not break the user experience
            _logger.LogError(ex, "Failed to track referral ID {ReferralId} for user {UserId}", 
                referralId, context.User?.Identity?.Name ?? "anonymous");
        }
        
        // Continue processing the request pipeline
        await _Next(context);
    }
}
}
