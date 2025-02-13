using System.Security.Claims;

namespace EssentialCSharp.Web.Services.Referrals;

public interface IReferralService
{
    Task TrackReferralAsync(string referralId, ClaimsPrincipal? user);
    Task<string?> GetReferralIdAsync(string userId);
}
