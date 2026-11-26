using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Extensions;

public static class UrlHelperExtensions
{
    /// <summary>
    /// Generates an absolute URL for a Razor page that is safe to put in an email. The origin comes from the
    /// configured <see cref="SiteSettings.BaseUrl"/>, never from the request's <c>Host</c> header, so a request
    /// with a forged <c>Host</c> cannot make the app email a link (and its token) to another site.
    /// </summary>
    /// <returns>The absolute URL, or <see langword="null"/> when the page cannot be resolved.</returns>
    public static string? EmailPageLink(this IUrlHelper url, SiteSettings siteSettings, string pageName, object? values)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(siteSettings);

        string? link = url.Page(pageName, pageHandler: null, values);
        if (link is null)
        {
            return null;
        }

        if (!Uri.TryCreate(siteSettings.BaseUrl, UriKind.Absolute, out Uri? baseUri))
        {
            throw new InvalidOperationException(
                $"{SiteSettings.SectionName}:{nameof(SiteSettings.BaseUrl)} must be an absolute URL to generate email links.");
        }

        // Keep only the path and query of whatever the URL helper produced, then attach the configured origin.
        string pathAndQuery = Uri.TryCreate(link, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps)
                ? absolute.PathAndQuery
                : link;
        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return baseUri.GetLeftPart(UriPartial.Authority) + pathAndQuery;
    }
}
