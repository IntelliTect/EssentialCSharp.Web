using System.Text.Json;

namespace EssentialCSharp.Web.Services;

public sealed class ChapterDescriptionService
{
    private readonly Dictionary<string, string> _Descriptions;

    public ChapterDescriptionService(IWebHostEnvironment environment)
    {
        string path = Path.Combine(environment.ContentRootPath, "Chapters", "sitemap.descriptions.json");
        if (!File.Exists(path))
        {
            _Descriptions = new Dictionary<string, string>();
            return;
        }

        using FileStream stream = File.OpenRead(path);
        List<SiteMappingDescription> entries = JsonSerializer.Deserialize<List<SiteMappingDescription>>(stream)
            ?? throw new InvalidDataException($"No chapter descriptions found in {path}");
        Dictionary<string, string> descriptions = new(StringComparer.OrdinalIgnoreCase);
        foreach (SiteMappingDescription entry in entries)
        {
            foreach (string key in entry.Keys)
            {
                descriptions.Add(key, entry.Value);
            }
        }
        _Descriptions = descriptions;
    }

    public string GetDescription(SiteMapping mapping)
    {
        foreach (string key in mapping.Keys)
        {
            if (_Descriptions.TryGetValue(key, out string? description) && !string.IsNullOrWhiteSpace(description))
            {
                return description;
            }
        }

        string heading = mapping.IndentLevel == 0 ? mapping.ChapterTitle : mapping.RawHeading;
        return $"Learn about {heading} in Essential C# with practical examples and explanations from the online C# guide.";
    }
}
