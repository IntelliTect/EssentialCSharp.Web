using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EssentialCSharp.Web.Services.Referrals;

public class ReferralService(EssentialCSharpWebContext dbContext, UserManager<EssentialCSharpWebUser> userManager) : IReferralService
{
    public async Task<string?> GetReferralIdAsync(string userId)
    {
        EssentialCSharpWebUser? user = await userManager.FindByIdAsync(userId);
        return await EnsureReferralIdAsync(user);
    }

    /// <summary>
    /// Ensure that the user has a referral ID. If the user does not have a referral ID, generate one and save it to the user.
    /// </summary>
    /// <param name="user"></param>
    /// <returns></returns>
    public async Task<string?> EnsureReferralIdAsync(EssentialCSharpWebUser? user)
    {
        if (user is null)
        {
            return null;
        }
        else
        {
            // Check if the user already has a referrer ID
            string? referrerId = dbContext.UserClaims.FirstOrDefault(claim => claim.UserId == user.Id && claim.ClaimType == ClaimsExtensions.ReferrerIdClaimType)?.ClaimValue;
            if (!string.IsNullOrEmpty(referrerId))
            {
                // Add the referrer ID to the user's claims if it does not exist
                if (!(await userManager.GetClaimsAsync(user)).Any(claim => claim.Type == ClaimsExtensions.ReferrerIdClaimType))
                {
                    await userManager.AddClaimAsync(user, new Claim(ClaimsExtensions.ReferrerIdClaimType, referrerId));
                }
                return referrerId;
            }
            else
            {
                do
                {
                    referrerId = Base64UrlEncoder.Encode(Guid.NewGuid().ToByteArray())[..8];
                }
                while (dbContext.UserClaims.Any(claim => claim.ClaimType == ClaimsExtensions.ReferrerIdClaimType && claim.ClaimValue == referrerId));

                await userManager.AddClaimAsync(user, new Claim(ClaimsExtensions.ReferrerIdClaimType, referrerId));
                return referrerId;
            }
        }
    }

    /// <summary>
    /// Track the referral in the database.
    /// </summary>
    /// <param name="referralId">The referrer ID to track.</param>
    /// <returns>True if the referral was successfully tracked, otherwise false.</returns>
    public void TrackReferralAsync(string referralId, ClaimsPrincipal? user)
    {
        // Check if the referrer ID exists in the claims principal
        string? claimsReferrerId = user?.Claims.FirstOrDefault(c => c.Type == ClaimsExtensions.ReferrerIdClaimType)?.Value;

        if (claimsReferrerId == referralId)
        {
            // If the referrer ID in the claims principal matches the referral ID, do not track the referral
            return;
        }

        TrackReferral(dbContext, referralId);
    }

    private static void TrackReferral(EssentialCSharpWebContext dbContext, string referralId)
    {
        var userClaim = dbContext.UserClaims.FirstOrDefault(claim => claim.ClaimType == ClaimsExtensions.ReferrerIdClaimType && claim.ClaimValue == referralId);
        if (userClaim is null)
        {
            return;
        }

        dbContext.Users.Where(user => user.Id == userClaim.UserId).ExecuteUpdate(setters => setters.SetProperty(b => b.ReferralCount, b => b.ReferralCount + 1));
    }
}
