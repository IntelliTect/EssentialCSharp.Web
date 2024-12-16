namespace EssentialCSharp.Web.Services;

// Data transfer object to pass necessary SiteMapping data info
// to frontend for use in table of contents
public class SiteMappingDto
{
    public required int Level { get; set; }
    public required string Key { get; set; }
    public required string Href { get; set; }
    public required string Title { get; set; }
    public required IEnumerable<SiteMappingDto> Items { get; set; }
}
