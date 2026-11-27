using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

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

    /// <summary>
    /// A non-secret marker of the signed-in user that the browser stores next to the AI chat history, so a page
    /// loaded by a different account (or after sign-out) can tell the stored conversation isn't its own.
    /// </summary>
    /// <returns>A hash of the user ID, or <see langword="null"/> when nobody is signed in.</returns>
    public static string? GetChatStorageOwner(this ClaimsPrincipal claimsPrincipal)
    {
        if (claimsPrincipal.Identity?.IsAuthenticated != true
            || claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } userId)
        {
            return null;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"EssentialCSharp.ChatStorageOwner:{userId}"));
        return Convert.ToHexString(hash, 0, 16);
    }

    public const string ReferrerIdClaimType = "ReferrerId";
}
