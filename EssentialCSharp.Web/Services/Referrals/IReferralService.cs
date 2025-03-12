using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;

namespace EssentialCSharp.Web.Services.Referrals;

public interface IReferralService
{
    void TrackReferralAsync(string referralId, ClaimsPrincipal? user);
    Task<string?> GetReferralIdAsync(string userId);
    Task<string?> EnsureReferralIdAsync(EssentialCSharpWebUser? user);
}
