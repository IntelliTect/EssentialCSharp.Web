using System.Globalization;

namespace EssentialCSharp.Web.Services;

public class SiteMappingService : ISiteMappingService
{
    public IList<SiteMapping> SiteMappings { get; }

    public SiteMappingService(IWebHostEnvironment webHostEnvironment)
    {
        string path = Path.Combine(webHostEnvironment.ContentRootPath, "Chapters", "sitemap.json");
        List<SiteMapping>? siteMappings = System.Text.Json.JsonSerializer.Deserialize<List<SiteMapping>>(File.OpenRead(path)) ?? throw new InvalidOperationException("No table of contents found");
        SiteMappings = siteMappings;
    }
}
