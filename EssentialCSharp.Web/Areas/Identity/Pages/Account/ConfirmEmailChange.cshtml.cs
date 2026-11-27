using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

// The link proves control of the new mailbox; being signed in as the account proves the change was requested by
// its owner. Requiring both stops someone attaching an address they don't own to their account (and keeping
// their own logins and tokens on it) just because the address owner clicked the link.
[Authorize]
public class ConfirmEmailChangeModel(UserManager<EssentialCSharpWebUser> userManager, SignInManager<EssentialCSharpWebUser> signInManager) : PageModel
{
    [TempData]
    public string StatusMessage { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (userId is null || email is null || code is null)
        {
            return RedirectToPage("/Index");
        }

        if (!string.Equals(userManager.GetUserId(User), userId, StringComparison.Ordinal))
        {
            StatusMessage = "Error changing email. Log in to the account that requested the change, then open the link again.";
            return Page();
        }

        EssentialCSharpWebUser? user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        IdentityResult result = await userManager.ChangeEmailAsync(user, email, code);
        if (!result.Succeeded)
        {
            StatusMessage = "Error changing email.";
            return Page();
        }

        // In our UI email and user name are one and the same, so when we update the email
        // we need to update the user name.
        IdentityResult setUserNameResult = await userManager.SetUserNameAsync(user, email);
        if (!setUserNameResult.Succeeded)
        {
            StatusMessage = "Error changing user name.";
            return Page();
        }

        await signInManager.RefreshSignInAsync(user);
        StatusMessage = "Thank you for confirming your email change.";
        return Page();
    }
}
