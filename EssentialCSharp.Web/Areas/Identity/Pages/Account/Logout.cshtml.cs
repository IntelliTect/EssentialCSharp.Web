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
        // Remove what the signed-in user left in browser storage (the AI chat history and its conversation ID),
        // so the next person to sign in on this browser doesn't see or continue it. Browsers honor this on HTTPS.
        Response.Headers["Clear-Site-Data"] = ClearSiteDataStorage;
        // This needs to be a redirect so that the browser performs a new
        // request and the identity for the user gets updated.
        return returnUrl is not null ? LocalRedirect(returnUrl) : RedirectToPage();
    }

    internal const string ClearSiteDataStorage = "\"storage\"";

    [LoggerMessage(Level = LogLevel.Information, Message = "User logged out.")]
    private static partial void LogUserLoggedOut(ILogger<LogoutModel> logger);
}
