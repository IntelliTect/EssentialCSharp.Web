using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Areas.Identity.Services;

/// <summary>
/// Remembers, in a protected cookie, which account this browser just registered. The first email confirmation
/// keeps the password chosen at registration only when the link is opened in that same browser; anywhere else,
/// the person clicking proved only that they own the mailbox, so they are asked to choose their own password.
/// </summary>
public sealed class RegistrationBrowserBinding(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<DataProtectionTokenProviderOptions> tokenOptions)
{
    internal const string CookieName = ".EssentialCSharp.RegistrationBinding";
    private const string CookiePath = "/Identity/Account";
    private const string Purpose = "EssentialCSharp.Web.RegistrationBrowserBinding.v1";

    // Confirmation links are emailed with the default token provider, so the binding lasts as long as the link.
    private TimeSpan Lifetime => tokenOptions.Value.TokenLifespan;

    private ITimeLimitedDataProtector Protector =>
        dataProtectionProvider.CreateProtector(Purpose).ToTimeLimitedDataProtector();

    public void Issue(HttpContext httpContext, string userId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrEmpty(userId);

        string value = Protector.Protect(userId, Lifetime);
        httpContext.Response.Cookies.Append(CookieName, value, CreateCookieOptions(httpContext, DateTimeOffset.UtcNow.Add(Lifetime)));
    }

    public bool IsBoundTo(HttpContext httpContext, string userId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (!httpContext.Request.Cookies.TryGetValue(CookieName, out string? value) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        try
        {
            return string.Equals(Protector.Unprotect(value), userId, StringComparison.Ordinal);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, tampered with or protected with a key that no longer exists.
            return false;
        }
    }

    public void Clear(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (httpContext.Request.Cookies.ContainsKey(CookieName))
        {
            httpContext.Response.Cookies.Delete(CookieName, CreateCookieOptions(httpContext, expires: null));
        }
    }

    private static CookieOptions CreateCookieOptions(HttpContext httpContext, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = httpContext.Request.IsHttps,
        // Lax so the cookie is sent when the user follows the emailed link (a top-level GET navigation).
        SameSite = SameSiteMode.Lax,
        Path = CookiePath,
        IsEssential = true,
        Expires = expires,
    };
}
