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

    /// <summary>The absolute path of the mapping's content file under <paramref name="contentRootPath"/>.</summary>
    /// <returns>The full path, or null when the mapping's page path resolves outside the content root.</returns>
    public static string? GetContentFilePath(this SiteMapping siteMapping, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(siteMapping);

        string root = Path.GetFullPath(contentRootPath);
        string full = Path.GetFullPath(Path.Join(root, Path.Join(siteMapping.PagePath)));
        string relative = Path.GetRelativePath(root, full);
        bool outsideRoot = Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
        return outsideRoot ? null : full;
    }
}
