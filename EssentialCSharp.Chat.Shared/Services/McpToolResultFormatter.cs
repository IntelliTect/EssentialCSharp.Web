using ModelContextProtocol.Protocol;

namespace EssentialCSharp.Chat.Common.Services;

public static class McpToolResultFormatter
{
    public static string GetModelInput(CallToolResult toolResult)
    {
        if (toolResult.StructuredContent is { } structuredContent)
        {
            return structuredContent.GetRawText();
        }

        return GetPrimaryTextContent(toolResult.Content);
    }

    public static string GetPrimaryTextContent(IEnumerable<ContentBlock> contentBlocks) =>
        contentBlocks
            .Where(x => x.Type == "text")
            .OfType<TextContentBlock>()
            .Select(x => x.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
        ?? string.Empty;
}
