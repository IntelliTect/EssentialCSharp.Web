using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators
{
    /// <summary>
    /// Provides an abstraction for validating that the supplied password is not in a list of common passwords
    /// </summary>
    public abstract class CommonPasswordValidator<TUser> : IPasswordValidator<TUser>
           where TUser : IdentityUser
    {

        public CommonPasswordValidator(HashSet<string> passwords)
        {
            Passwords = passwords;
        }

        /// <summary>
        /// The collection of common passwords which should not be allowed
        /// </summary>
        protected HashSet<string> Passwords { get; }

        ///<inheritdoc />
        public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager,
                                                  TUser user,
                                                  string? password)
        {
            if (password == null) { throw new ArgumentNullException(nameof(password)); }
            if (manager == null) { throw new ArgumentNullException(nameof(manager)); }

            IdentityResult result = Passwords.Contains(password, StringComparer.InvariantCultureIgnoreCase)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "CommonPassword",
                Description = "The password you chose is too common."
            })
            : IdentityResult.Success;

            return Task.FromResult(result);
        }

    }
}
