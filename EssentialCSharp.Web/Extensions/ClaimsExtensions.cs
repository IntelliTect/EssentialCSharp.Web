using System.Security.Claims;

namespace EssentialCSharp.Web.Extensions;

public static class ClaimsExtensions
{
    public static string? GetReferrerId(this ClaimsPrincipal claimsPrincipal)
    {
        return claimsPrincipal.FindFirstValue(ReferrerIdClaimType);
    }

    public static string? GetReferrerId(this IList<Claim> claims)
    {
        return claims.FirstOrDefault(claim => claim.Type == ReferrerIdClaimType)?.Value;
    }

    /// <summary>
    /// Gets the referral ID from the current user's claims in the HttpContext
    /// </summary>
    /// <param name="httpContext">The HttpContext to get the referral ID from</param>
    /// <returns>The referral ID if found, otherwise null</returns>
    public static string? GetReferrerId(this HttpContext? httpContext)
    {
        return httpContext?.User?.GetReferrerId();
    }

    public const string ReferrerIdClaimType = "ReferrerId";
}
