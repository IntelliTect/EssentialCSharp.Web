using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;

/// <summary>
/// Validates that the supplied password is not one of the 100,000+ most common passwords
/// </summary>
public class Top100000PasswordValidator<TUser>(PasswordLists passwords)
    : CommonPasswordValidator<TUser>(passwords.Top100000PasswordsPlus.Value) where TUser : IdentityUser
{
    public Top100000PasswordValidator() : this(new PasswordLists())
    { }
}
