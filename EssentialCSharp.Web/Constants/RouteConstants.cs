using System.Collections.Frozen;
using DotnetSitemapGenerator;

namespace EssentialCSharp.Web.Constants;

/// <summary>
/// Centralized definition of application routes and their metadata.
/// This is the single source of truth for static page route paths.
/// Update these whenever a static page is added, removed, or renamed.
/// </summary>
public static class RouteConstants
{
    /// <summary>
    /// Static page routes that are not content pages (e.g., informational, utility pages).
    /// Content pages are dynamically discovered from sitemap.json.
    /// </summary>
    public static class StaticPages
    {
        public const string Home = "/home";
        public const string About = "/about";
        public const string Guidelines = "/guidelines";
        public const string Announcements = "/announcements";
        public const string TermsOfService = "/termsofservice";
    }

    /// <summary>
    /// Immutable set of non-content route paths. Use to determine if a requested path
    /// is a static page (non-content) rather than a content page (from sitemap).
    /// </summary>
    public static readonly FrozenSet<string> NonContentRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        StaticPages.Home,
        StaticPages.About,
        StaticPages.Guidelines,
        StaticPages.Announcements,
        StaticPages.TermsOfService
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// SEO metadata for static routes used in sitemap.xml generation.
    /// </summary>
    public static class SeoMetadata
    {
        /// <summary>
        /// Immutable map of route paths to (ChangeFrequency, Priority) tuples.
        /// Keys include the leading slash (e.g. "/home") to match how ASP.NET Core
        /// exposes routes. Priority is 0.0–1.0; 0.5 is the sitemap default.
        /// </summary>
        public static readonly FrozenDictionary<string, (ChangeFrequency Frequency, decimal Priority)> RouteConfig =
            new Dictionary<string, (ChangeFrequency, decimal)>(StringComparer.OrdinalIgnoreCase)
            {
                { StaticPages.Home,          (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.About,         (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.Guidelines,    (ChangeFrequency.Monthly, 0.9m) },
                { StaticPages.Announcements, (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.TermsOfService,(ChangeFrequency.Yearly,  0.2m) }
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
