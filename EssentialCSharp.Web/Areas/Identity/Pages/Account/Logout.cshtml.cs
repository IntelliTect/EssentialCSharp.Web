using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public class LogoutModel : PageModel
{

    private readonly SignInManager<EssentialCSharpWebUser> _SignInManager;
    private readonly ILogger<LogoutModel> _Logger;

    public LogoutModel(SignInManager<EssentialCSharpWebUser> signInManager, ILogger<LogoutModel> logger)
    {
        _SignInManager = signInManager;
        _Logger = logger;
    }

    public async Task<IActionResult> OnPost(string? returnUrl = null)
    {
        await _SignInManager.SignOutAsync();
        _Logger.LogInformation("User logged out.");
        if (returnUrl is not null)
        {
            return LocalRedirect(returnUrl);
        }
        else
        {
            // This needs to be a redirect so that the browser performs a new
            // request and the identity for the user gets updated.
            return RedirectToPage();
        }
    }
}
