using System.Collections.Generic;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace EssentialCSharp.Web.Models;

public static class McpToolResultFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static CallToolResult CreateHybridResult<T>(string readableText, T structuredContent)
    {
        string jsonText = JsonSerializer.Serialize(structuredContent, JsonOptions);

        return new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                CreateTextContentBlock(readableText),
                CreateTextContentBlock(jsonText)
            },
            StructuredContent = JsonSerializer.SerializeToElement(structuredContent, JsonOptions)
        };
    }

    public static CallToolResult CreateError(string message) =>
        new()
        {
            IsError = true,
            Content = new List<ContentBlock>
            {
                CreateTextContentBlock(message)
            }
        };

    private static TextContentBlock CreateTextContentBlock(string text) => new() { Text = text };
}
