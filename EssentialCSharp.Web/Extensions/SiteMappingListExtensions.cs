using EssentialCSharp.Common;
using System.Globalization;

namespace EssentialCSharp.Web.Extensions;

public static class SiteMappingListExtensions
{
    /// <summary>
    /// Finds a site mapping based on a key.
    /// </summary>
    /// <param name="siteMappings">IList of SiteMappings</param>
    /// <param name="key">If null, uses the first key in the list</param>
    /// <returns>If found, the site mapping that matches the key, otherwise null.</returns>
    public static SiteMapping? Find(this IList<SiteMapping> siteMappings, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return siteMappings.FirstOrDefault();
        }
        foreach (string? potentialMatch in key.GetPotentialMatches())
        {
            if (siteMappings.FirstOrDefault(x => x.Key == potentialMatch) is { } siteMap)
            {
                return siteMap;
            }
        }
        return null;
    }
    /// <summary>
    /// Finds percent complete based on a key.
    /// </summary>
    /// <param name="siteMappings">IList of SiteMappings</param>
    /// <param name="key">If null, uses the first key in the list</param>
    /// <returns>Returns a formatted double for use as the percent complete.</returns>
    public static string? FindPercentComplete(this IList<SiteMapping> siteMappings, string? key)
    {
        if (key is null)
        {
            return null;
        }
        if (key.Trim().Length is 0)
        {
            throw new ArgumentException("Parameter key is whitespace or empty: ", nameof(key));
        }
        int currentMappingCount = 0;
        int overallMappingCount = 0;
        bool currentPageFound = false;
        IEnumerable<IGrouping<int, SiteMapping>> chapterGroupings = siteMappings.GroupBy(x => x.ChapterNumber).OrderBy(g => g.Key);
        foreach (IGrouping<int, SiteMapping> chapterGrouping in chapterGroupings)
        {
            IEnumerable<IGrouping<int, SiteMapping>> pageGroupings = chapterGrouping.GroupBy(x => x.PageNumber).OrderBy(g => g.Key);
            foreach (IGrouping<int, SiteMapping> pageGrouping in pageGroupings)
            {
                foreach (SiteMapping siteMapping in pageGrouping)
                {
                    if (!currentPageFound)
                    {
                        currentMappingCount++;
                    }
                    overallMappingCount++;
                    if (siteMapping.Key == key)
                    {
                        currentPageFound = true;
                    }
                }
            }
        }
        if (overallMappingCount is 0)
        {
            return "0.00";
        }
        double result = (double)currentMappingCount / overallMappingCount * 100;
        return string.Format(CultureInfo.InvariantCulture, "{0:0.00}", result);
    }
}
