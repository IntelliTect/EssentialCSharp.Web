using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators
{
    public class UsernameOrEmailAsPasswordValidator<TUser> : IPasswordValidator<TUser>
        where TUser : IdentityUser
    {
        public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
        {
            if (string.Equals(user.UserName, password, StringComparison.OrdinalIgnoreCase)
                || string.Equals(user.Email, password, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(IdentityResult.Failed(new IdentityError
                {
                    Code = "UsernameOrEmailAsPassword",
                    Description = "You cannot use your username or email as your password"
                }));
            }
            return Task.FromResult(IdentityResult.Success);
        }
    }
}
