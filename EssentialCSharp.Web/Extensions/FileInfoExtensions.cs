using System.Text.Json;

namespace EssentialCSharp.Web.Extensions;

public static partial class FileInfoExtensions
{
    public static List<GuidelineListing>? ReadGuidelineJsonFromInputDirectory(this FileInfo guidelinesJsonFile, ILogger logger)
    {
        // Check if the file exists
        if (!guidelinesJsonFile.Exists)
        {
            LogFileNotFound(logger, guidelinesJsonFile.FullName);
            return null;
        }

        // Read the JSON file
        string jsonString = File.ReadAllText(guidelinesJsonFile.FullName);

        // Deserialize the JSON string into a List<GuidelineListing>
        List<GuidelineListing>? guidelines = JsonSerializer.Deserialize<List<GuidelineListing>>(jsonString, GuidelineListing.Options);

        if (guidelines?.Count > 0)
        {
            LogGuidelinesRead(logger, guidelinesJsonFile.FullName);
        }

        return guidelines;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "File not found at {JsonPath}")]
    private static partial void LogFileNotFound(ILogger logger, string jsonPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "guidelines.json successfully read from {JsonPath}")]
    private static partial void LogGuidelinesRead(ILogger logger, string jsonPath);
}
