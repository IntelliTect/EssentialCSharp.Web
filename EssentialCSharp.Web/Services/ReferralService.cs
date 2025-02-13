using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Sqids;

namespace EssentialCSharp.Web.Services;

public class ReferralService(EssentialCSharpWebContext dbContext, SqidsEncoder<int> sqids, UserManager<EssentialCSharpWebUser> userManager) : IReferralService
{
    public async Task<string?> GetReferralIdAsync(string userId)
    {
        EssentialCSharpWebUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }
        else
        {
            // Check if the user already has a referrer ID
            if (!string.IsNullOrEmpty(user.ReferrerId))
            {
                return user.ReferrerId;
            }
            else
            {
                Random random = new();
                string referrerId = sqids.Encode(random.Next());
                user.ReferrerId = referrerId;

                await userManager.AddClaimAsync(user, new Claim("ReferrerId", referrerId));
                await userManager.UpdateAsync(user);
                return user.ReferrerId;
            }
        }
    }

    /// <summary>
    /// Track the referral in the database.
    /// </summary>
    /// <param name="referralId">The referrer ID to track.</param>
    /// <returns>True if the referral was successfully tracked, otherwise false.</returns>
    public async Task<bool> TrackReferralAsync(string referralId, ClaimsPrincipal? user)
    {
        EssentialCSharpWebUser? claimsUser = user is null ? null : await userManager.GetUserAsync(user);
        if (claimsUser is null)
        {
            return await TrackReferral(dbContext, referralId);
        }
        else
        {
            // If the user is the referrer, do not track the referral
            if (claimsUser.ReferrerId == referralId)
            {
                return false;
            }
            else
            {
                return await TrackReferral(dbContext, referralId);
            }
        }

        static async Task<bool> TrackReferral(EssentialCSharpWebContext dbContext, string referralId)
        {
            EssentialCSharpWebUser? dbUser = await dbContext.Users.SingleOrDefaultAsync(u => u.ReferrerId == referralId);
            if (dbUser is null)
            {
                return false;
            }
            else
            {
                dbUser.ReferralCount++;
                await dbContext.SaveChangesAsync();
                return true;
            }
        }
    }
}
