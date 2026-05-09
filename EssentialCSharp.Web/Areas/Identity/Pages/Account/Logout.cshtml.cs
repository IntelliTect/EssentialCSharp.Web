using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public partial class LogoutModel(SignInManager<EssentialCSharpWebUser> signInManager, ILogger<LogoutModel> logger) : PageModel
{
    public async Task<IActionResult> OnPost(string? returnUrl = null)
    {
        await signInManager.SignOutAsync();
        LogUserLoggedOut(logger);
        // This needs to be a redirect so that the browser performs a new
        // request and the identity for the user gets updated.
        return returnUrl is not null ? LocalRedirect(returnUrl) : RedirectToPage();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged out.")]
    private static partial void LogUserLoggedOut(ILogger<LogoutModel> logger);
}
