using System.Text.Json;

namespace EssentialCSharp.Web.Extensions;

public static class FileInfoExtensions
{
    public static List<GuidelineListing>? ReadGuidelineJsonFromInputDirectory(this FileInfo guidelinesJsonFile, ILogger logger)
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
        List<GuidelineListing>? guidelines = JsonSerializer.Deserialize<List<GuidelineListing>>(jsonString, GuidelineListing.Options);

        if (guidelines != null && guidelines.Count > 0)
        {
            logger.LogInformation("guidelines.json successfully read from {JsonPath}", guidelinesJsonFile.FullName);
        }

        return guidelines;
    }
}
