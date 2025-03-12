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
    public const string ReferrerIdClaimType = "ReferrerId";
}
