namespace EssentialCSharp.Web.Services;

public class SiteMappingDto
{
    public required int Level { get; set; }
    public required List<string> Keys { get; set; }
    public required string Href { get; set; }
    public required string Title { get; set; }
    public required IEnumerable<SiteMappingDto> Items { get; set; }
}
