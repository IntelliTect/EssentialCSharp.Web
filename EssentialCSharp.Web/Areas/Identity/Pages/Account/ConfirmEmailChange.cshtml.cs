using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class ConfirmEmailChangeModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;

    public ConfirmEmailChangeModel(UserManager<EssentialCSharpWebUser> userManager, SignInManager<EssentialCSharpWebUser> signInManager)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
    }

    [TempData]
    public string StatusMessage { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string? userId, string? email, string? code)
    {
        if (userId is null || email is null || code is null)
        {
            return RedirectToPage("/Index");
        }

        EssentialCSharpWebUser? user = await _UserManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userId}'.");
        }

        code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        IdentityResult result = await _UserManager.ChangeEmailAsync(user, email, code);
        if (!result.Succeeded)
        {
            StatusMessage = "Error changing email.";
            return Page();
        }

        // In our UI email and user name are one and the same, so when we update the email
        // we need to update the user name.
        IdentityResult setUserNameResult = await _UserManager.SetUserNameAsync(user, email);
        if (!setUserNameResult.Succeeded)
        {
            StatusMessage = "Error changing user name.";
            return Page();
        }

        await _SignInManager.RefreshSignInAsync(user);
        StatusMessage = "Thank you for confirming your email change.";
        return Page();
    }
}
