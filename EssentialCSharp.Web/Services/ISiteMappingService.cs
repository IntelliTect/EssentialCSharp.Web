namespace EssentialCSharp.Web.Services;

public interface ISiteMappingService
{
    IList<SiteMapping> SiteMappings { get; }
    IEnumerable<SiteMappingDto> GetTocData();
    string GetPercentComplete(string currentPageKey);
}
