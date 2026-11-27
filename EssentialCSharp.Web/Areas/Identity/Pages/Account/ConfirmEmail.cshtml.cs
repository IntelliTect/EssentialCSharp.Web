using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class ConfirmEmailModel(
    UserManager<EssentialCSharpWebUser> userManager,
    SignInManager<EssentialCSharpWebUser> signInManager,
    AccountOwnershipService accountOwnershipService,
    RegistrationBrowserBinding registrationBrowserBinding) : PageModel
{
    [TempData]
    public string StatusMessage { get; set; } = string.Empty;

    /// <summary>Set when the person confirming must choose a password before the account can be used.</summary>
    public string? SetPasswordUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(string? userId, string? code, CancellationToken cancellationToken = default)
    {
        if (userId is null || code is null)
        {
            return RedirectToPage("/Index");
        }

        EssentialCSharpWebUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        bool wasConfirmed = user.EmailConfirmed;
        code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        IdentityResult result = await userManager.ConfirmEmailAsync(user, code);
        if (!result.Succeeded)
        {
            StatusMessage = "Error confirming your email.";
            return Page();
        }

        StatusMessage = "Thank you for confirming your email.";

        if (!wasConfirmed)
        {
            // Opening the link proves control of the mailbox, and nothing else. Anything attached to the account
            // before now may belong to whoever typed this address, so it does not survive confirmation.
            await accountOwnershipService.RemoveCredentialsAddedBeforeOwnershipProofAsync(user, cancellationToken);

            if (await userManager.HasPasswordAsync(user))
            {
                if (registrationBrowserBinding.IsBoundTo(HttpContext, user.Id))
                {
                    registrationBrowserBinding.Clear(HttpContext);
                }
                else
                {
                    // The password was chosen by whoever registered, and this link was opened in another
                    // browser. Don't let that password unlock the account; have the mailbox owner choose one.
                    await userManager.RemovePasswordAsync(user);
                    string resetCode = await userManager.GeneratePasswordResetTokenAsync(user);
                    SetPasswordUrl = Url.Page(
                        "./ResetPassword",
                        pageHandler: null,
                        values: new { code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(resetCode)) });
                    StatusMessage = "Thank you for confirming your email. To finish, choose a password for your account. "
                        + "(If you registered on another device, this keeps the account safe in case someone else chose your password.)";
                    return Page();
                }
            }
        }

        // Link an external login that was waiting for this confirmation, but only if this browser holds it.
        ExternalLoginInfo? externalLogin = await signInManager.GetExternalLoginInfoAsync();
        if (await accountOwnershipService.TryLinkPendingExternalLoginAsync(user, externalLogin))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            StatusMessage = $"Thank you for confirming your email. Your {externalLogin!.ProviderDisplayName ?? externalLogin.LoginProvider} account is linked, so you can now log in with it.";
        }
        else if (await accountOwnershipService.GetPendingExternalLoginDisplayNameAsync(user) is string pendingProvider
            && !await userManager.HasPasswordAsync(user)
            && (await userManager.GetLoginsAsync(user)).Count == 0)
        {
            StatusMessage = $"Thank you for confirming your email. To finish, log in with {pendingProvider} again and "
                + "register with this email; then open the new link in that same browser.";
        }

        return Page();
    }
}
