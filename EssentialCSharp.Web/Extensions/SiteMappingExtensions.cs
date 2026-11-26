namespace EssentialCSharp.Web.Extensions;

public static class SiteMappingExtensions
{
    /// <summary>
    /// The key that identifies the mapping's page to the reading tracker and the percent-complete lookup.
    /// Placeholder sitemaps (CI, local dev) may omit PrimaryKey, so the first key is preferred.
    /// </summary>
    /// <returns>The first non-blank key, else the primary key, else null (the page is then not tracked).</returns>
    public static string? GetPageKey(this SiteMapping siteMapping)
    {
        ArgumentNullException.ThrowIfNull(siteMapping);

        string? pageKey = siteMapping.Keys?.FirstOrDefault(k => !string.IsNullOrWhiteSpace(k)) ?? siteMapping.PrimaryKey;
        return string.IsNullOrWhiteSpace(pageKey) ? null : pageKey;
    }
}
