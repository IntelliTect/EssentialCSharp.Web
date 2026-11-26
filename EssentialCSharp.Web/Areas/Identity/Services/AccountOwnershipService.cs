using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;

namespace EssentialCSharp.Web.Areas.Identity.Services;

/// <summary>
/// Keeps credentials off an account until the person adding them has proven they control the account's email
/// address, and clears anything attached before that proof. Without this, someone who registers (or links an
/// external login to) an address they don't own keeps a way in after the real owner confirms or resets it.
/// </summary>
public sealed class AccountOwnershipService(
    UserManager<EssentialCSharpWebUser> userManager,
    McpApiTokenService mcpApiTokenService)
{
    // Stored in AspNetUserTokens. The bracketed provider name cannot collide with a real external provider.
    internal const string PendingExternalLoginTokenProvider = "[EssentialCSharpPendingExternalLogin]";
    internal const string PendingExternalLoginTokenName = "Login";

    /// <summary>
    /// Records the external login that asked to use this account. It is linked only by
    /// <see cref="TryLinkPendingExternalLoginAsync"/>, after the email is confirmed in a browser that still holds
    /// the same external login.
    /// </summary>
    public Task<IdentityResult> SetPendingExternalLoginAsync(EssentialCSharpWebUser user, UserLoginInfo login)
    {
        ArgumentNullException.ThrowIfNull(login);
        string value = JsonSerializer.Serialize(new PendingExternalLogin(login.LoginProvider, login.ProviderKey, login.ProviderDisplayName));
        return userManager.SetAuthenticationTokenAsync(user, PendingExternalLoginTokenProvider, PendingExternalLoginTokenName, value);
    }

    /// <summary>Returns the display name of the provider whose login is waiting to be linked, if any.</summary>
    public async Task<string?> GetPendingExternalLoginDisplayNameAsync(EssentialCSharpWebUser user)
    {
        PendingExternalLogin? pending = await GetPendingExternalLoginAsync(user);
        return pending is null ? null : pending.ProviderDisplayName ?? pending.LoginProvider;
    }

    /// <summary>
    /// Links <paramref name="login"/> to <paramref name="user"/> only when it is the login recorded by
    /// <see cref="SetPendingExternalLoginAsync"/>. Call it only after the caller has proven control of the email.
    /// </summary>
    /// <returns><see langword="true"/> when the login was linked.</returns>
    public async Task<bool> TryLinkPendingExternalLoginAsync(EssentialCSharpWebUser user, UserLoginInfo? login)
    {
        if (login is null)
        {
            return false;
        }

        PendingExternalLogin? pending = await GetPendingExternalLoginAsync(user);
        if (pending is null
            || !string.Equals(pending.LoginProvider, login.LoginProvider, StringComparison.Ordinal)
            || !string.Equals(pending.ProviderKey, login.ProviderKey, StringComparison.Ordinal))
        {
            return false;
        }

        IdentityResult result = await userManager.AddLoginAsync(user, login);
        if (!result.Succeeded)
        {
            return false;
        }

        await userManager.RemoveAuthenticationTokenAsync(user, PendingExternalLoginTokenProvider, PendingExternalLoginTokenName);
        return true;
    }

    /// <summary>
    /// Call when someone has just proven control of the account's email (first confirmation or a password reset).
    /// Removes every external login and revokes every MCP token, because any of them may have been added by
    /// someone else before that proof, then rotates the security stamp so existing sessions and tokens end.
    /// </summary>
    public async Task RemoveCredentialsAddedBeforeOwnershipProofAsync(EssentialCSharpWebUser user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        foreach (UserLoginInfo login in await userManager.GetLoginsAsync(user))
        {
            IdentityResult removed = await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            if (!removed.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not remove the {login.LoginProvider} login: {string.Join("; ", removed.Errors.Select(e => e.Description))}");
            }
        }

        await mcpApiTokenService.RevokeAllTokensAsync(user.Id, cancellationToken);

        IdentityResult stampResult = await userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not rotate the security stamp: {string.Join("; ", stampResult.Errors.Select(e => e.Description))}");
        }
    }

    private async Task<PendingExternalLogin?> GetPendingExternalLoginAsync(EssentialCSharpWebUser user)
    {
        string? value = await userManager.GetAuthenticationTokenAsync(user, PendingExternalLoginTokenProvider, PendingExternalLoginTokenName);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PendingExternalLogin>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record PendingExternalLogin(string LoginProvider, string ProviderKey, string? ProviderDisplayName);
}
