using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public class DownloadPersonalDataModel : PageModel
{
    private readonly UserManager<EssentialCSharpWebUser> _UserManager;
    private readonly ILogger<DownloadPersonalDataModel> _Logger;

    public DownloadPersonalDataModel(
        UserManager<EssentialCSharpWebUser> userManager,
        ILogger<DownloadPersonalDataModel> logger)
    {
        _UserManager = userManager;
        _Logger = logger;
    }

    public IActionResult OnGet()
    {
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser? user = await _UserManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{_UserManager.GetUserId(User)}'.");
        }

        _Logger.LogInformation("User with ID '{UserId}' asked for their personal data.", _UserManager.GetUserId(User));

        // Only include personal data for download
        var personalData = new Dictionary<string, string>();
        IEnumerable<System.Reflection.PropertyInfo> personalDataProps = typeof(EssentialCSharpWebUser).GetProperties().Where(
                        prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
        foreach (System.Reflection.PropertyInfo p in personalDataProps)
        {
            personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
        }

        IList<UserLoginInfo> logins = await _UserManager.GetLoginsAsync(user);
        foreach (UserLoginInfo l in logins)
        {
            personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
        }
        string? authenticatorKey = await _UserManager.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrWhiteSpace(authenticatorKey))
        {
            personalData.Add($"Authenticator Key", authenticatorKey);
        }

        Response.Headers.Append("Content-Disposition", "attachment; filename=PersonalData.json");
        return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(personalData), "application/json");
    }
}
