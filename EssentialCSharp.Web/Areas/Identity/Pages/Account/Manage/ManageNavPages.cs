using Microsoft.AspNetCore.Mvc.Rendering;

namespace EssentialCSharp.Web.Areas.Identity.Pages.Account.Manage;

/// <summary>
///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
///     directly from your code. This API may change or be removed in future releases.
/// </summary>
public static class ManageNavPages
{
    public static string Index => "Index";

    public static string Email => "Email";

    public static string ChangePassword => "ChangePassword";

    public static string DownloadPersonalData => "DownloadPersonalData";

    public static string DeletePersonalData => "DeletePersonalData";

    public static string ExternalLogins => "ExternalLogins";

    public static string PersonalData => "PersonalData";

    public static string TwoFactorAuthentication => "TwoFactorAuthentication";

    public static string Referrals => "Referrals";

    public static string? IndexNavClass(ViewContext viewContext) => PageNavClass(viewContext, Index);

    public static string? EmailNavClass(ViewContext viewContext) => PageNavClass(viewContext, Email);

    public static string? ChangePasswordNavClass(ViewContext viewContext) => PageNavClass(viewContext, ChangePassword);

    public static string? DownloadPersonalDataNavClass(ViewContext viewContext) => PageNavClass(viewContext, DownloadPersonalData);

    public static string? DeletePersonalDataNavClass(ViewContext viewContext) => PageNavClass(viewContext, DeletePersonalData);

    public static string? ExternalLoginsNavClass(ViewContext viewContext) => PageNavClass(viewContext, ExternalLogins);

    public static string? PersonalDataNavClass(ViewContext viewContext) => PageNavClass(viewContext, PersonalData);

    public static string? TwoFactorAuthenticationNavClass(ViewContext viewContext) => PageNavClass(viewContext, TwoFactorAuthentication);

    public static string? ReferralsNavClass(ViewContext viewContext) => PageNavClass(viewContext, Referrals);

    public static string? PageNavClass(ViewContext viewContext, string page)
    {
        string? activePage = viewContext.ViewData["ActivePage"] as string
            ?? Path.GetFileNameWithoutExtension(viewContext.ActionDescriptor.DisplayName);
        return string.Equals(activePage, page, StringComparison.OrdinalIgnoreCase) ? "active" : null;
    }
}
