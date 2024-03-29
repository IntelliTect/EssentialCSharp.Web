﻿using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;

/// <summary>
/// Provides an abstraction for validating that the supplied password is not in a list of common passwords
/// </summary>
public abstract class CommonPasswordValidator<TUser>(HashSet<string> passwords) : IPasswordValidator<TUser>
       where TUser : IdentityUser
{

    /// <summary>
    /// The collection of common passwords which should not be allowed
    /// </summary>
    protected HashSet<string> Passwords { get; } = passwords;

    ///<inheritdoc />
    public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager,
                                              TUser user,
                                              string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(password);

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
