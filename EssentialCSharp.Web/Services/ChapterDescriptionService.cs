using System.Text.Json;

namespace EssentialCSharp.Web.Services;

public sealed class ChapterDescriptionService
{
    private readonly Dictionary<string, string> _Descriptions;

    /// <summary>Number of descriptions loaded from sitemap.descriptions.json (0 when the file is absent).</summary>
    public int Count => _Descriptions.Count;

    public ChapterDescriptionService(IWebHostEnvironment environment)
    {
        string path = Path.Combine(environment.ContentRootPath, "Chapters", "sitemap.descriptions.json");
        if (!File.Exists(path))
        {
            _Descriptions = new Dictionary<string, string>();
            return;
        }

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Expected a JSON array of chapter descriptions in {path}");
        }

        Dictionary<string, string> descriptions = new(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            string value = ReadValue(entry, path, index);
            foreach (string key in ReadKeys(entry, path, index))
            {
                descriptions.Add(key, value);
            }
            index++;
        }
        _Descriptions = descriptions;
    }

    // The content package has emitted { "Key": "..." } while the shared SiteMappingDescription type
    // declares "Keys". Accept both so a mismatch between the two cannot take the site down.
    private static List<string> ReadKeys(JsonElement entry, string path, int index)
    {
        if (entry.ValueKind == JsonValueKind.Object)
        {
            if (entry.TryGetProperty("Keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
            {
                List<string> result = [];
                foreach (JsonElement key in keys.EnumerateArray())
                {
                    string? text = key.ValueKind == JsonValueKind.String ? key.GetString() : null;
                    result.Add(!string.IsNullOrWhiteSpace(text) ? text : throw InvalidEntry(path, index, "contains an empty key"));
                }
                return result;
            }

            if (entry.TryGetProperty("Key", out JsonElement singleKey) && singleKey.ValueKind == JsonValueKind.String)
            {
                string? text = singleKey.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return [text];
                }
            }
        }

        throw InvalidEntry(path, index, "has neither a \"Keys\" array nor a \"Key\" string");
    }

    private static string ReadValue(JsonElement entry, string path, int index)
    {
        if (entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("Value", out JsonElement value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is string text)
        {
            return text;
        }

        throw InvalidEntry(path, index, "has no \"Value\" string");
    }

    private static InvalidDataException InvalidEntry(string path, int index, string problem) =>
        new($"Chapter description entry {index} in {path} {problem}");

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
