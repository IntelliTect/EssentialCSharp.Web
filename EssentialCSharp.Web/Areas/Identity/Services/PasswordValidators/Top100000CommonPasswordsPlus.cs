using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators
{
    /// <summary>
    /// Validates that the supplied password is not one of the 100,000+ most common passwords
    /// </summary>
    public class Top100000PasswordValidator<TUser>
        : CommonPasswordValidator<TUser> where TUser : IdentityUser
    {
        public Top100000PasswordValidator() : this(new())
        {
        }
        public Top100000PasswordValidator(PasswordLists passwords)
            : base(passwords.Top100000PasswordsPlus.Value)
        { }
    }
}
