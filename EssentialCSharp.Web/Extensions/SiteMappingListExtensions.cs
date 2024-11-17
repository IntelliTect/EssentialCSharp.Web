using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Extensions;

public static class SiteMappingListExtensions
{
    /// <summary>
    /// Finds a site mapping based on a key.
    /// </summary>
    /// <param name="siteMappings">IList of SiteMappings</param>
    /// <param name="key">If null, uses the first key in the list</param>
    /// <returns>If found, the site mapping that matches the key, otherwise null.</returns>
    public static Services.SiteMapping? Find(this IList<Services.SiteMapping> siteMappings, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return siteMappings.FirstOrDefault();
        }
        foreach (string? potentialMatch in key.GetPotentialMatches())
        {
            if (siteMappings.FirstOrDefault(x => x.Keys.Any(k => k == potentialMatch)) is { } siteMap)
            {
                return siteMap;
            }
        }
        return null;
    }
}
