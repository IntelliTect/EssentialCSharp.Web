using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using ModelContextProtocol.Protocol;

namespace EssentialCSharp.Chat.Tests;

public class McpToolResultFormatterTests
{
    [Test]
    public async Task GetModelInput_PrefersStructuredContent_WhenAvailable()
    {
        JsonElement structuredContent = JsonSerializer.SerializeToElement(new
        {
            diagnostic = "CS8600",
            relevantSections = Array.Empty<object>()
        });

        CallToolResult toolResult = new()
        {
            Content =
            [
                new TextContentBlock { Text = "# Book Help for: CS8600" },
                new TextContentBlock { Text = "{\"diagnostic\":\"CS8600\"}" }
            ],
            StructuredContent = structuredContent
        };

        string modelInput = McpToolResultFormatter.GetModelInput(toolResult);

        await Assert.That(modelInput).IsEqualTo(structuredContent.GetRawText());
    }

    [Test]
    public async Task GetModelInput_FallsBackToFirstTextBlock_WhenStructuredContentIsMissing()
    {
        CallToolResult toolResult = new()
        {
            Content =
            [
                new TextContentBlock { Text = "# Readable content" },
                new TextContentBlock { Text = "{\"json\":\"fallback\"}" }
            ]
        };

        string modelInput = McpToolResultFormatter.GetModelInput(toolResult);

        await Assert.That(modelInput).IsEqualTo("# Readable content");
    }
}
