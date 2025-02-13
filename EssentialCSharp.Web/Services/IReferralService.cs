using System.Security.Claims;

namespace EssentialCSharp.Web.Services;

public interface IReferralService
{
    Task<bool> TrackReferralAsync(string referralId, ClaimsPrincipal? user);
    Task<string?> GetReferralIdAsync(string userId);
}
