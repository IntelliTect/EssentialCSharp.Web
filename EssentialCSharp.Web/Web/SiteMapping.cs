using EssentialCSharp.Web.Extensions;

namespace EssentialCSharp.Web;

public record class SiteMapping(string Key, string[] PagePath, int ChapterNumber, int PageNumber, string ChapterTitle, string RawHeading, string? AnchorId)
{
    public static SiteMapping? Find(string key, IList<SiteMapping> siteMappings)
    {
        key ??= siteMappings[0].Key;
        if (key.StartsWith("Home/", StringComparison.InvariantCultureIgnoreCase))
        {
            key = key.Remove(0, "Home/".Length);
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
}
