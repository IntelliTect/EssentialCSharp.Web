namespace EssentialCSharp.Web.Constants;

/// <summary>
/// Centralized definition of application routes and their metadata.
/// This constant provides a single source of truth for route paths,
/// making it easier to maintain and update routes across the application.
/// </summary>
public static class RouteConstants
{
    /// <summary>
    /// Static page routes that are not content pages (e.g., informational, utility pages).
    /// Content pages are dynamically loaded from sitemap.json.
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
    /// Set of non-content route paths. Use this to determine if a requested path
    /// is a static page (non-content) or a content page (from sitemap).
    /// </summary>
    public static readonly HashSet<string> NonContentRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        StaticPages.Home,
        StaticPages.About,
        StaticPages.Guidelines,
        StaticPages.Announcements,
        StaticPages.TermsOfService
    };

    /// <summary>
    /// SEO metadata for routes used in sitemap generation.
    /// Maps route paths to their change frequency and priority values.
    /// </summary>
    public static class SeoMetadata
    {
        public enum ChangeFrequency
        {
            Always,
            Hourly,
            Daily,
            Weekly,
            Monthly,
            Yearly,
            Never
        }

        /// <summary>
        /// Maps route paths to (ChangeFrequency, Priority) tuples for sitemap.xml generation.
        /// Priority is a decimal value between 0.0 and 1.0 (0.5 is the default).
        /// </summary>
        public static readonly Dictionary<string, (ChangeFrequency, decimal)> RouteConfig =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { StaticPages.Home, (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.About, (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.Guidelines, (ChangeFrequency.Monthly, 0.9m) },
                { StaticPages.Announcements, (ChangeFrequency.Monthly, 0.5m) },
                { StaticPages.TermsOfService, (ChangeFrequency.Yearly, 0.2m) }
            };
    }

    /// <summary>
    /// Determines if the given path represents a content page (from sitemap)
    /// versus a static page (non-content).
    /// </summary>
    public static bool IsContentPage(string path)
    {
        return !NonContentRoutes.Contains(path);
    }
}
