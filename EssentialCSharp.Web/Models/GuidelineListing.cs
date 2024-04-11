using System.Text.Json;

namespace EssentialCSharp.Web.Models;

public record GuidelineListing(GuidelineType Type, string Guideline, int ChapterNumber, string? ChapterTitle, string SanitizedSubsection, string? ActualSubsection)
{
    private static readonly JsonSerializerOptions _Options = new() { WriteIndented = true };

    public static GuidelineType GetGuidelineType(IReadOnlyList<string> selectionString)
    {
        if (selectionString.Count < 2)
        {
            return GuidelineType.None;
        }

        if (selectionString[1].Equals("not", StringComparison.OrdinalIgnoreCase))
        {
            return GuidelineType.DoNot;
        }

        if (Enum.TryParse<GuidelineType>(selectionString[0], true, out GuidelineType result))
        {
            return result;
        }
        else
        {
            return GuidelineType.None;
        }
    }

    public static List<GuidelineListing>? ReadGuidelineJsonFromInputDirectory(FileInfo guidelinesJsonFile, ILogger logger)
    {
        // Check if the file exists
        if (!guidelinesJsonFile.Exists)
        {
            logger.LogError("File not found at {JsonPath}", guidelinesJsonFile.FullName);
            return null;
        }

        // Read the JSON file
        string jsonString = File.ReadAllText(guidelinesJsonFile.FullName);

        // Deserialize the JSON string into a List<GuidelineListing>
        List<GuidelineListing>? guidelines = JsonSerializer.Deserialize<List<GuidelineListing>>(jsonString, _Options);

        if (guidelines != null && guidelines.Count > 0)
        {
            logger.LogInformation("guidelines.json successfully read from {JsonPath}", guidelinesJsonFile.FullName);
        }

        return guidelines;
    }
}

public enum GuidelineType
{
    None,
    DoNot,
    Avoid,
    Consider,
    Do
}
