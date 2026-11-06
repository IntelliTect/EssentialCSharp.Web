using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

public partial class DownloadPersonalDataModel(
    UserManager<EssentialCSharpWebUser> userManager,
    EssentialCSharpWebContext context,
    ILogger<DownloadPersonalDataModel> logger) : PageModel
{
    public IActionResult OnGet()
    {
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        EssentialCSharpWebUser? user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        LogUserAskedForPersonalData(logger, userManager.GetUserId(User));

        // Only include personal data for download
        var personalData = new Dictionary<string, string>();
        IEnumerable<System.Reflection.PropertyInfo> personalDataProps = typeof(EssentialCSharpWebUser).GetProperties().Where(
                        prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
        foreach (System.Reflection.PropertyInfo p in personalDataProps)
        {
            personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
        }

        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(user);
        foreach (UserLoginInfo l in logins)
        {
            personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
        }
        string? authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrWhiteSpace(authenticatorKey))
        {
            personalData.Add($"Authenticator Key", authenticatorKey);
        }

        UserReadingProfile? readingProfile = await context.UserReadingProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id);
        if (readingProfile is not null)
        {
            personalData.Add("Reading total words read", readingProfile.TotalWordsRead.ToString(System.Globalization.CultureInfo.InvariantCulture));
            personalData.Add("Reading total active seconds", readingProfile.TotalActiveSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            personalData.Add("Reading profile last updated (UTC)", readingProfile.UpdatedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }

        Response.Headers.Append("Content-Disposition", "attachment; filename=PersonalData.json");
        return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(personalData), "application/json");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' asked for their personal data.")]
    private static partial void LogUserAskedForPersonalData(ILogger<DownloadPersonalDataModel> logger, string? userId);
}
