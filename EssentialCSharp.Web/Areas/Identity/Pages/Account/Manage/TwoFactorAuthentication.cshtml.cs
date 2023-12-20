using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class TwoFactorAuthenticationModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly ILogger<TwoFactorAuthenticationModel> _Logger;

    public TwoFactorAuthenticationModel(
        UserManager<EssentialCSharpWebUser> userManager, SignInManager<EssentialCSharpWebUser> signInManager, ILogger<TwoFactorAuthenticationModel> logger)
    {
        _UserManager = userManager;
        _SignInManager = signInManager;
        _Logger = logger;
    }

    public bool HasAuthenticator { get; set; }

    public int RecoveryCodesLeft { get; set; }

    [BindProperty]
    public bool Is2faEnabled { get; set; }

    public bool IsMachineRemembered { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        HasAuthenticator = await _UserManager.GetAuthenticatorKeyAsync(user) != null;
        Is2faEnabled = await _UserManager.GetTwoFactorEnabledAsync(user);
        IsMachineRemembered = await _SignInManager.IsTwoFactorClientRememberedAsync(user);
        RecoveryCodesLeft = await _UserManager.CountRecoveryCodesAsync(user);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        await _SignInManager.ForgetTwoFactorClientAsync();
        StatusMessage = "The current browser has been forgotten. When you login again from this browser you will be prompted for your 2fa code.";
        return RedirectToPage();
    }
}
