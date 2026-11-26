using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The placeholders in <c>Placeholders/</c> stand in for the content package in PR builds, so they must have
/// the same JSON shape. When the build uses the real package (<c>-p:AccessToNugetFeed=true</c>) the content
/// files differ from the placeholders and this fails if the package changed shape. Without the package the
/// content files are the placeholders, so it passes trivially. Only property names and value kinds are
/// compared, and failures report only those, so no book content reaches a public log.
/// </summary>
public class PlaceholderShapeTests : IntegrationTestBase
{
    [Test]
    [Arguments("Chapters/sitemap.json", "sitemap.json")]
    [Arguments("Chapters/sitemap.descriptions.json", "sitemap.descriptions.json")]
    [Arguments("Guidelines/guidelines.json", "guidelines.json")]
    public async Task PlaceholderMatchesContentShape(string contentPath, string placeholderName)
    {
        string root = Factory.Inner.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        string placeholderPath = Path.Combine(root, "Placeholders", placeholderName);
        string actualPath = Path.Combine(root, contentPath);

        Dictionary<string, HashSet<string>> placeholder = ReadShape(placeholderPath);
        Dictionary<string, HashSet<string>> actual = ReadShape(actualPath);

        List<string> differences = [];
        foreach (string name in placeholder.Keys.Except(actual.Keys))
        {
            differences.Add($"property \"{name}\" is only in the placeholder");
        }
        foreach (string name in actual.Keys.Except(placeholder.Keys))
        {
            differences.Add($"property \"{name}\" is only in the content package");
        }
        foreach (string name in placeholder.Keys.Intersect(actual.Keys))
        {
            // Nulls are ignored: optional fields may be null throughout one file and populated in the other.
            if (placeholder[name].Count > 0 && actual[name].Count > 0 && !placeholder[name].SetEquals(actual[name]))
            {
                differences.Add($"property \"{name}\" is {string.Join("/", placeholder[name].Order())} in the placeholder but {string.Join("/", actual[name].Order())} in the content package");
            }
        }

        await Assert.That(differences)
            .IsEmpty()
            .Because($"Placeholders/{placeholderName} must mirror {contentPath} from the content package: update the placeholder to the package's shape. {string.Join("; ", differences)}");
    }

    // Maps each property name used by the entries of a JSON array to the non-null value kinds seen for it
    // (arrays are described by their element kinds, e.g. "Array<String>").
    private static Dictionary<string, HashSet<string>> ReadShape(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"Expected a JSON array in {Path.GetFileName(path)}");
        }

        Dictionary<string, HashSet<string>> shape = [];
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Expected every entry in {Path.GetFileName(path)} to be an object");
            }

            foreach (JsonProperty property in entry.EnumerateObject())
            {
                if (!shape.TryGetValue(property.Name, out HashSet<string>? kinds))
                {
                    shape[property.Name] = kinds = [];
                }

                if (property.Value.ValueKind != JsonValueKind.Null)
                {
                    kinds.Add(Describe(property.Value));
                }
            }
        }

        return shape;
    }

    private static string Describe(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array
            ? $"Array<{string.Join("|", value.EnumerateArray().Select(Describe).Distinct().Order())}>"
            : value.ValueKind.ToString();
}
