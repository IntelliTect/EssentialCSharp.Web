using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Data;

// Add profile data for application users by adding properties to the EssentialCSharpWebUser class
public class EssentialCSharpWebUser : IdentityUser
{
    [ProtectedPersonalData]
    public virtual string? FirstName { get; set; }
    [ProtectedPersonalData]
    public virtual string? LastName { get; set; }
    public string? ReferrerId { get; set; }
    public int ReferralCount { get; set; }
}

