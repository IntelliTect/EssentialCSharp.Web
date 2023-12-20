using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class ExternalLoginsModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly IUserPasswordStore<EssentialCSharpWebUser> _UserPasswordStore;

    public ExternalLoginsModel(
        UserManager<EssentialCSharpWebUser> userManager,
        SignInManager<EssentialCSharpWebUser> signInManager,
        IUserPasswordStore<EssentialCSharpWebUser> userPasswordStore)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
        _UserPasswordStore = userPasswordStore;
    }

    public IList<UserLoginInfo>? CurrentLogins { get; set; }

    public IList<AuthenticationScheme>? OtherLogins { get; set; }

    public bool ShowRemoveButton { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        CurrentLogins = await _UserManager.GetLoginsAsync(user);
        OtherLogins = (await _SignInManager.GetExternalAuthenticationSchemesAsync())
            .Where(auth => CurrentLogins.All(ul => auth.Name != ul.LoginProvider))
            .ToList();

        string? passwordHash = null;
        passwordHash = await _UserPasswordStore.GetPasswordHashAsync(user, HttpContext.RequestAborted);

        ShowRemoveButton = passwordHash is not null || CurrentLogins.Count > 1;
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveLoginAsync(string loginProvider, string providerKey)
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        IdentityResult result = await _UserManager.RemoveLoginAsync(user, loginProvider, providerKey);
        if (!result.Succeeded)
        {
            StatusMessage = "The external login was not removed.";
            return RedirectToPage();
        }

        await _SignInManager.RefreshSignInAsync(user);
        StatusMessage = "The external login was removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLinkLoginAsync(string provider)
    {
        // Clear the existing external cookie to ensure a clean login process
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Request a redirect to the external login provider to link a login for the current user
        string? redirectUrl = Url.Page("./ExternalLogins", pageHandler: "LinkLoginCallback");
        AuthenticationProperties properties = _SignInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl, _UserManager.GetUserId(User));
        return new ChallengeResult(provider, properties);
    }

    public async Task<IActionResult> OnGetLinkLoginCallbackAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        string userId = await _UserManager.GetUserIdAsync(user);
        ExternalLoginInfo info = await _SignInManager.GetExternalLoginInfoAsync(userId) ?? throw new InvalidOperationException($"Unexpected error occurred loading external login info.");
        IdentityResult result = await _UserManager.AddLoginAsync(user, info);
        if (!result.Succeeded)
        {
            StatusMessage = "The external login was not added. External logins can only be associated with one account.";
            return RedirectToPage();
        }

        // Clear the existing external cookie to ensure a clean login process
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        StatusMessage = "The external login was added.";
        return RedirectToPage();
    }
}
