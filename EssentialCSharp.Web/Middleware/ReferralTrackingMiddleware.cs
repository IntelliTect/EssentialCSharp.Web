using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Middleware;

public class ReferralMiddleware
{
    private readonly RequestDelegate _Next;

    public ReferralMiddleware(RequestDelegate next)
    {
        _Next = next;
    }

    public async Task InvokeAsync(HttpContext context, IReferralService referralService, UserManager<EssentialCSharpWebUser> userManager)
    {
        // Retrieve current referral Id for processing
        string referralId = context.Request.Query["rid"].ToString();
        string? userReferralId;

        if (context.User is { } claimsUser && claimsUser.Identity is not null && claimsUser.Identity.IsAuthenticated)
        {
            await TrackReferralAsync(referralService, referralId, claimsUser);

            // Add the referralId to the request context if it exists on a user
            EssentialCSharpWebUser? user = await userManager.GetUserAsync(claimsUser);
            if (user is not null)
            {
                var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                userReferralId = await referralService.GetReferralIdAsync(user.Id);

                if (!query.ContainsKey("rid") || (userReferralId is not null && query.TryGetValue("rid", out Microsoft.Extensions.Primitives.StringValues values) && !values.Contains(userReferralId)))
                {
                    query.Remove("rid");
                    var newQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(context.Request.Path.Value!, "rid", "TEST");
                    context.Response.Redirect(newQuery);
                }
                //context.Items["rid"] = userReferralId;
                //var parametersToAdd = new System.Collections.Generic.Dictionary<string, string> { { "rid", userReferralId } };
                //var someUrl = context.Request;
                //var newUri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(someUrl, parametersToAdd);
                //if (!string.IsNullOrWhiteSpace(userReferralId))
                //{
                //    QueryBuilder queryBuilder = new QueryBuilder();
                //    if (context.Request.QueryString.HasValue)
                //    {
                //        foreach (var key in context.Request.Query.Keys)
                //        {
                //            var realValue = context.Request.Query[key];
                //            var modifiedValue = HttpUtility.UrlDecode(realValue);
                //            queryBuilder.Add(key, modifiedValue);
                //        }
                //    }
                //    queryBuilder.Add("rid", "TestRID");
                //    context.Request.QueryString = queryBuilder.ToQueryString();
                //}
            }
        }
        else
        {
            await TrackReferralAsync(referralService, referralId, null);
        }

        await _Next(context);

        static async Task TrackReferralAsync(IReferralService referralService, string? referralId, ClaimsPrincipal? claimsUser)
        {
            if (!string.IsNullOrWhiteSpace(referralId))
            {
                _ = await referralService.TrackReferralAsync(referralId, claimsUser);
            }
        }
    }
}
