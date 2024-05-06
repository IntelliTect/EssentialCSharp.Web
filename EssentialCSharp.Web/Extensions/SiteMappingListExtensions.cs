namespace EssentialCSharp.Web.Extensions;

public static class SiteMappingListExtensions
{
    public static SiteMapping? Find(this IList<SiteMapping> siteMappings, string key)
    {
        key = siteMappings[0].Key;
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
