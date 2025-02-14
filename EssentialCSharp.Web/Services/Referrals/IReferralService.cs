using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;

namespace EssentialCSharp.Web.Services.Referrals;

public interface IReferralService
{
    Task TrackReferralAsync(string referralId, ClaimsPrincipal? user);
    Task<string?> GetReferralIdAsync(string userId);
    Task<string?> GetReferralIdAsync(EssentialCSharpWebUser? user);
}
